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
        static ParticlePoolCollapseJobDelegate poolJob = ParticlePoolCollapseJob<FlowSuperPosition>.ScheduleParallel;

        private NativeArray<float> tmp;
        
        [NativeDisableContainerSafetyRestriction]
        private NativeArray<Cardinal> flow;
        
        [NativeDisableContainerSafetyRestriction]
        private NativeStream stream;
        public override void ResizeNativeContainers(int size){
            // Resize containers
            
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                stream.Dispose();
            }
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flow = new NativeArray<Cardinal>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            stream = new NativeStream(size, Allocator.Persistent);
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
            JobHandle second = poolJob(
                d.data,
                tmp,
                flow,
                stream,
                d.resolution,
                first
            );
            jobHandle = TileHelpers.SWAP_RWTILE(d.data, tmp, second);
            // jobHandle = TileHelpers.SWAP_RWTILE(d.data, tmp, first);
        }

        public override void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
                flow.Dispose();
                stream.Dispose();
            }
        }
    }
}