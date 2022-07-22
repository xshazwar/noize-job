using System;
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

        private NativeArray<float> tmp;
        
        [NativeDisableContainerSafetyRestriction]
        private NativeArray<Cardinal> flow;
        
        [NativeDisableContainerSafetyRestriction]
        private NativeStream stream;
        private NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima;
        private NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers;
        private NativeParallelHashMap<int, int> catchmentMap;
        
        [NativeDisableContainerSafetyRestriction]
        private NativeList<int> minimas;
        private NativeList<PoolKey> drainKeys;
        private NativeParallelHashMap<PoolKey, Pool> pools;
        private NativeParallelMultiHashMap<PoolKey, int> drainToMinima;
        private int currentSize = 0;
        public override void ResizeNativeContainers(int size){
            // Resize containers
            
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                boundaryMapMemberToMinima.Dispose();
                boundaryMapMinimaToMembers.Dispose();
                catchmentMap.Dispose();
                pools.Dispose();
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
            boundaryMapMemberToMinima = new NativeParallelMultiHashMap<int, int>(size, Allocator.Persistent);
            boundaryMapMinimaToMembers = new NativeParallelMultiHashMap<int, int>(size, Allocator.Persistent);
            catchmentMap = new NativeParallelHashMap<int, int>(dataLength, Allocator.Persistent);
            pools = new NativeParallelHashMap<PoolKey, Pool>(dataLength, Allocator.Persistent);
        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
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
            JobHandle seventh = DebugDrawAndCleanUpJob.ScheduleRun(d.data, tmp, boundaryMapMemberToMinima, boundaryMapMinimaToMembers, catchmentMap, pools, d.resolution, sixth, !Draw2d);
            jobHandle = TileHelpers.SWAP_RWTILE(d.data, tmp, seventh);
        }

        public void Clean(){
            if(tmp.IsCreated){
                minimas.Clear();
                boundaryMapMemberToMinima.Clear();
                boundaryMapMinimaToMembers.Clear();
                catchmentMap.Clear();
            }
            if(pools.IsCreated){
                pools.Clear();
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
                boundaryMapMemberToMinima.Dispose();
                boundaryMapMinimaToMembers.Dispose();
                catchmentMap.Dispose();
                pools.Dispose();
                drainKeys.Dispose();
                drainToMinima.Dispose();
                minimas.Dispose();
            }
        }
    }
}