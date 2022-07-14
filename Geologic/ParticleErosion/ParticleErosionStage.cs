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



        static ParticlePoolMinimaJobDelegate minimaJob = ParticlePoolMinimaJob<FlowSuperPosition>.ScheduleParallel;
        static ParticlePoolCollapseJobDelegate poolCollapseJob = ParticlePoolCollapseJob<FlowSuperPosition>.ScheduleParallel;
        static PoolCreationJobDelegate poolCreateJob = PoolCreationJob<FlowSuperPosition>.Schedule;

        private NativeArray<float> tmp;
        
        [NativeDisableContainerSafetyRestriction]
        private NativeArray<Cardinal> flow;
        
        [NativeDisableContainerSafetyRestriction]
        private NativeStream stream;

        private NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima;
        private NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers;
        private NativeParallelHashMap<int, int> catchmentMap;
        public override void ResizeNativeContainers(int size){
            // Resize containers
            
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                stream.Dispose();
                boundaryMapMemberToMinima.Dispose();
                boundaryMapMinimaToMembers.Dispose();
                catchmentMap.Dispose();
            }
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flow = new NativeArray<Cardinal>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            stream = new NativeStream(size, Allocator.Persistent);
            boundaryMapMemberToMinima = new NativeParallelMultiHashMap<int, int>(size, Allocator.Persistent);
            boundaryMapMinimaToMembers = new NativeParallelMultiHashMap<int, int>(size, Allocator.Persistent);
            catchmentMap = new NativeParallelHashMap<int, int>(dataLength, Allocator.Persistent);
        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
            JobHandle first = minimaJob(
                d.data,
                tmp,
                flow,
                stream,
                d.resolution,
                dependency
            );
            JobHandle second = poolCollapseJob(
                d.data,
                tmp,
                flow,
                stream,
                boundaryMapMemberToMinima,
                boundaryMapMinimaToMembers,
                catchmentMap,
                d.resolution,
                first
            );
            JobHandle third = poolCreateJob(
                d.data,
                tmp,
                boundaryMapMemberToMinima,boundaryMapMinimaToMembers,
                catchmentMap,
                d.resolution,
                second
            );
            jobHandle = TileHelpers.SWAP_RWTILE(d.data, tmp, third);
            // jobHandle = TileHelpers.SWAP_RWTILE(d.data, tmp, first);
        }

        public override void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                stream.Dispose();
                boundaryMapMemberToMinima.Dispose();
                boundaryMapMinimaToMembers.Dispose();
                catchmentMap.Dispose();
            }
        }
    }
}