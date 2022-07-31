using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;


using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.geologic {

    [CreateAssetMenu(fileName = "ParticleErosion", menuName = "Noize/Geologic/ParticleErosion", order = 2)]
    public class ParticleErosionStage: PipelineStage {

        public bool Draw2d = false;


        static ParticlePoolMinimaJobDelegate minimaJob = ParticlePoolMinimaJob.ScheduleParallel;
        static ParticlePoolCollapseJobDelegate poolCollapseJob = ParticlePoolCollapseJob.ScheduleParallel;
        static DrainSolvingJobDelegate drainSolveJob = DrainSolvingJob.Schedule;
        static LockJobDelegate lockJob = LockJob.Schedule;

        private NativeArray<float> tmp;
        
        [NativeDisableContainerSafetyRestriction]
        private NativeArray<Cardinal> flow;
        
        [NativeDisableContainerSafetyRestriction]
        private NativeStream stream;

        [NativeDisableContainerSafetyRestriction]
        private NativeList<int> minimas;
        private NativeList<PoolKey> drainKeys;
        private NativeParallelMultiHashMap<PoolKey, int> drainToMinima;
        private int currentSize = 0;

        private string getBufferName(GeneratorData d, string alias){
            return $"{d.xpos}_{d.zpos}__{d.resolution}__{alias}";
        }

        public override bool IsSchedulable(PipelineWorkItem job){
            if(job.stageManager == null){
                return false;
            }
            bool[] notReady = new bool[] {
                job.stageManager.IsLocked<NativeParallelMultiHashMap<int, int>>(getBufferName((GeneratorData)job.data,"PARTERO_BOUNDARY_BM")),
                job.stageManager.IsLocked<NativeParallelMultiHashMap<int, int>>(getBufferName((GeneratorData)job.data,"PARTERO_BOUNDARY_MB")),
                job.stageManager.IsLocked<NativeParallelHashMap<int, int>>(getBufferName((GeneratorData)job.data,"PARTERO_CATCHMENT")),
                job.stageManager.IsLocked<NativeParallelHashMap<PoolKey, Pool>>(getBufferName((GeneratorData)job.data,"PARTERO_POOLS"))
            };
            if(notReady.Contains<bool>(true)){
                return false;
            }
            return true;
        }
        public override void ResizeNativeContainers(int size){
            // Resize containers
            
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                drainKeys.Dispose();
                drainToMinima.Dispose();
                minimas.Dispose();
            }
            currentSize = size;
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flow = new NativeArray<Cardinal>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            minimas = new NativeList<int>(32, Allocator.Persistent);
            drainKeys = new NativeList<PoolKey>(size, Allocator.Persistent);
            drainToMinima = new NativeParallelMultiHashMap<PoolKey, int>(dataLength, Allocator.Persistent);
        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
            // TODO write a native container that we can resize inside the generation job
            // so that we don't have to live with oversized Persistent allocations everywhere
            //  https://forum.unity.com/threads/how-to-allocate-nativecontainer-inside-long-running-job.902963/
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima = requirements.stageManager.GetBuffer<int, int, NativeParallelMultiHashMap<int, int>>(getBufferName((GeneratorData)requirements.data,"PARTERO_BOUNDARY_BM"), currentSize);
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers = requirements.stageManager.GetBuffer<int, int, NativeParallelMultiHashMap<int, int>>(getBufferName((GeneratorData)requirements.data,"PARTERO_BOUNDARY_MB"), currentSize);
            NativeParallelHashMap<int, int> catchmentMap = requirements.stageManager.GetBuffer<int, int, NativeParallelHashMap<int, int>>(getBufferName((GeneratorData)requirements.data,"PARTERO_CATCHMENT"), dataLength);
            NativeParallelHashMap<PoolKey, Pool> pools = requirements.stageManager.GetBuffer<PoolKey, Pool, NativeParallelHashMap<PoolKey, Pool>>(getBufferName((GeneratorData)requirements.data,"PARTERO_POOLS"), 512);
            Clean();
            stream = new NativeStream(currentSize, Allocator.Persistent);
            JobHandle first = minimaJob(
                d.data,
                tmp,
                flow,
                stream,
                d.resolution,
                dependency
            );
            JobHandle reduce = ParticleWriteMinimas.ScheduleRun(d.resolution, minimas, stream, first);
            JobHandle second = poolCollapseJob(
                d.data,
                tmp,
                flow,
                minimas,
                boundaryMapMemberToMinima,
                boundaryMapMinimaToMembers,
                catchmentMap,
                d.resolution,
                reduce
            );
            JobHandle third = stream.Dispose(second);
            JobHandle fourth = drainSolveJob(
                d.data,
                tmp,
                boundaryMapMemberToMinima,
                boundaryMapMinimaToMembers,
                catchmentMap,
                pools,
                drainKeys,
                drainToMinima,
                d.resolution,
                second
            );
            JobHandle fifth = PoolCreationJob.ScheduleParallel(
                d.data, tmp, drainKeys, drainToMinima, catchmentMap, boundaryMapMinimaToMembers, pools, d.resolution, fourth);
            JobHandle sixth = SolvePoolHeirarchyJob.ScheduleRun(drainToMinima, pools, fifth);
            JobHandle lockHandle = lockJob(sixth);
            JobHandle seventh = PoolDrawDebugAndCleanUpJob.ScheduleRun(d.data, tmp, boundaryMapMemberToMinima, boundaryMapMinimaToMembers, catchmentMap, pools, d.resolution, lockHandle, !Draw2d);
            jobHandle = TileHelpers.SWAP_RWTILE(d.data, tmp, seventh);
            // Only locking one of the 4 buffers because I'm feeling lazy... this will bite the ass later
            string bufferName = getBufferName((GeneratorData)requirements.data,"PARTERO_POOLS");
            requirements.stageManager.TrySetLock<NativeParallelHashMap<PoolKey, Pool>>(bufferName, lockHandle, sixth);
        }

        public void Clean(){
            if(tmp.IsCreated){
                minimas.Clear();
            }
            if(drainKeys.IsCreated){
                drainKeys.Clear();
            }
            if(drainToMinima.IsCreated){
                drainToMinima.Clear();
            }
        }

        public override void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                drainKeys.Dispose();
                drainToMinima.Dispose();
                minimas.Dispose();
            }
        }
    }
}