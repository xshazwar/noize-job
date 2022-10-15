using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections.LowLevel.Unsafe;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.geologic {
    
    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct CleanJob : IJob {

        NativeList<int>minimas;
        NativeList<PoolKey> drainKeys;
        NativeParallelHashMap<PoolKey, Pool> pools;
        NativeParallelMultiHashMap<int, int> boundary_BM;
        NativeParallelMultiHashMap<int, int> boundary_MB;
        NativeParallelMultiHashMap<PoolKey, int> drainToMinima;
        NativeParallelHashMap<int, int> catchmentMap;

        public void Execute(){
            minimas.Clear();
            drainKeys.Clear();
            pools.Clear();
            boundary_BM.Clear();
            boundary_MB.Clear();
            drainToMinima.Clear();
            catchmentMap.Clear();
        }
        public static JobHandle ScheduleRun(
            NativeList<int>minimas,
            NativeList<PoolKey> drainKeys,
            NativeParallelHashMap<PoolKey, Pool> pools,
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelMultiHashMap<int, int> boundary_MB,
            NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            NativeParallelHashMap<int, int> catchmentMap,
            JobHandle deps
        ){
            var job = new CleanJob {
                minimas = minimas,
                drainKeys = drainKeys,
                pools = pools,
                boundary_BM  = boundary_BM,
                boundary_MB = boundary_MB,
                drainToMinima = drainToMinima,
                catchmentMap = catchmentMap
            };
            
            return job.Schedule(deps);
        }
    }
    
    public class PoolMetaData {
        public int xpos;
        public int zpos;
        public int resolution;
    }
    public class PoolSolver{

        private PoolMetaData meta;
        private PipelineStateManager state;
        bool Draw2d = false;
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
        private int currentSize = 0;
        private int dataLength = 0;

        public PoolSolver(int xpos, int zpos, int resolution, PipelineStateManager state){
            meta = new PoolMetaData {
                xpos = xpos,
                zpos = zpos,
                resolution = resolution
            };
            this.state = state;
            ResizeNativeContainers(resolution);
        }

        public PoolSolver(){
            meta = new PoolMetaData();
        }

        public string getBufferName(string alias){
            return $"{meta.xpos}_{meta.zpos}__{meta.resolution}__{alias}";
        }

        public void SetMetaFromData(GeneratorData d){
            meta.xpos = d.xpos;
            meta.zpos = d.zpos;
            meta.resolution = d.resolution;
        }
        public bool IsSchedulable(){
            if(state == null){
                return false;
            }
            bool[] notReady = new bool[] {
                state.IsLocked<NativeParallelMultiHashMap<int, int>>(getBufferName("PARTERO_BOUNDARY_BM")),
                state.IsLocked<NativeParallelMultiHashMap<int, int>>(getBufferName("PARTERO_BOUNDARY_MB")),
                state.IsLocked<NativeParallelMultiHashMap<PoolKey, int>>(getBufferName("PARTERO_DRAIN_TO_MINIMA")),
                state.IsLocked<NativeParallelHashMap<int, int>>(getBufferName("PARTERO_CATCHMENT")),
                state.IsLocked<NativeParallelHashMap<PoolKey, Pool>>(getBufferName("PARTERO_POOLS"))
            };
            if(notReady.Contains<bool>(true)){
                return false;
            }
            return true;
        }

        public void ResizeNativeContainers(int size){
            // Resize containers
            dataLength = size * size;
            Debug.Log($"new solver resolution {dataLength}");
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                drainKeys.Dispose();
                minimas.Dispose();
            }
            currentSize = size;
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flow = new NativeArray<Cardinal>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            minimas = new NativeList<int>(32, Allocator.Persistent);
            drainKeys = new NativeList<PoolKey>(size, Allocator.Persistent);
        }

        public JobHandle Schedule(PipelineWorkItem requirements, JobHandle dependency){
            // CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
            SetMetaFromData(d);
            state = requirements.stageManager;
            NativeSlice<float> heights = d.data;
            return Schedule(heights, dependency);
        }

        public JobHandle Schedule(NativeSlice<float> heights, JobHandle dependency){
            int dataLength = heights.Length;

            // TODO write a native container that we can resize inside the generation job
            // so that we don't have to live with oversized Persistent allocations everywhere
            //  https://forum.unity.com/threads/how-to-allocate-nativecontainer-inside-long-running-job.902963/
            
            NativeParallelMultiHashMap<int, int> boundary_BM = state.GetBuffer<int, int, NativeParallelMultiHashMap<int, int>>(getBufferName("PARTERO_BOUNDARY_BM"), dataLength);
            NativeParallelMultiHashMap<int, int> boundary_MB = state.GetBuffer<int, int, NativeParallelMultiHashMap<int, int>>(getBufferName("PARTERO_BOUNDARY_MB"), dataLength);
            // TODO ^^ resize these buffers after they're complete to save memory 
            NativeParallelMultiHashMap<PoolKey, int> drainToMinima = state.GetBuffer<PoolKey, int, NativeParallelMultiHashMap<PoolKey, int>>(getBufferName("PARTERO_DRAIN_TO_MINIMA"), dataLength);
            NativeParallelHashMap<int, int> catchmentMap = state.GetBuffer<int, int, NativeParallelHashMap<int, int>>(getBufferName("PARTERO_CATCHMENT"), dataLength);
            NativeParallelHashMap<PoolKey, Pool> pools = state.GetBuffer<PoolKey, Pool, NativeParallelHashMap<PoolKey, Pool>>(getBufferName("PARTERO_POOLS"), 2048);
            
            JobHandle clean = CleanJob.ScheduleRun(minimas, drainKeys, pools, boundary_BM, boundary_MB, drainToMinima, catchmentMap, dependency);
            stream = new NativeStream(dataLength, Allocator.Persistent);
            JobHandle first = minimaJob(
                heights,
                tmp,
                flow,
                stream,
                meta.resolution,
                clean
            );
            JobHandle reduce = ParticleWriteMinimas.ScheduleRun(meta.resolution, minimas, stream, first);
            JobHandle second = poolCollapseJob(
                heights,
                tmp,
                flow,
                minimas,
                boundary_BM,
                boundary_MB,
                catchmentMap,
                meta.resolution,
                reduce
            );
            JobHandle third = stream.Dispose(second);
            JobHandle fourth = drainSolveJob(
                heights,
                tmp,
                boundary_BM,
                boundary_MB,
                catchmentMap,
                pools,
                drainKeys,
                drainToMinima,
                meta.resolution,
                second
            );
            JobHandle fifth = PoolCreationJob.ScheduleParallel(
                heights, tmp, drainKeys, drainToMinima, catchmentMap, boundary_MB, pools, meta.resolution, fourth);
            JobHandle sixth = SolvePoolHeirarchyJob.ScheduleRun(drainToMinima, pools, fifth);
            // JobHandle lockHandle = lockJob(sixth);
            // JobHandle seventh = PoolDrawDebugAndCleanUpJob.ScheduleRun(heights, tmp, boundary_BM, boundary_MB, catchmentMap, pools, meta.resolution, lockHandle, !Draw2d);
            // JobHandle handle = TileHelpers.SWAP_RWTILE(heights, tmp, seventh);
            // // Only locking one of the 4 buffers because I'm feeling lazy... this will bite the ass later
            // string bufferName = getBufferName("PARTERO_POOLS");
            // state.TrySetLock<NativeParallelHashMap<PoolKey, Pool>>(bufferName, lockHandle, sixth);
            // return handle;
            return sixth;
        }

        // public void Clean(){
        //     if(tmp.IsCreated){
        //         minimas.Clear();
        //     }
        //     if(drainKeys.IsCreated){
        //         drainKeys.Clear();
        //     }
        // }

        public void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                drainKeys.Dispose();
                minimas.Dispose();
            }
        }





    }
}