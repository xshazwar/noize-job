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
        private UnsafeList<PoolKey> poolKeys;
        private UnsafeParallelHashMap<PoolKey, Pool> pools;

        private int currentSize = 0;
        public override void ResizeNativeContainers(int size){
            // Resize containers
            
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                boundaryMapMemberToMinima.Dispose();
                boundaryMapMinimaToMembers.Dispose();
                catchmentMap.Dispose();
            }
            currentSize = size;
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flow = new NativeArray<Cardinal>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            minimas = new NativeList<int>(32, Allocator.Persistent);
            boundaryMapMemberToMinima = new NativeParallelMultiHashMap<int, int>(size, Allocator.Persistent);
            boundaryMapMinimaToMembers = new NativeParallelMultiHashMap<int, int>(size, Allocator.Persistent);
            catchmentMap = new NativeParallelHashMap<int, int>(dataLength, Allocator.Persistent);
        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
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
                d.resolution,
                second
            );
            jobHandle = TileHelpers.SWAP_RWTILE(d.data, tmp, fourth);
            // jobHandle = TileHelpers.SWAP_RWTILE(d.data, tmp, first);
        }

        public override void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                minimas.Dispose();
                boundaryMapMemberToMinima.Dispose();
                boundaryMapMinimaToMembers.Dispose();
                catchmentMap.Dispose();
            }
            if(pools.IsCreated){
                pools.Dispose();
            }
        }
    }
}