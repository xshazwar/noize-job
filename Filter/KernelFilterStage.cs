using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "KernelFilter", menuName = "Noize/Filter/KernelFilter", order = 2)]
    public class KernelFilterStage: PipelineStage {

        static SeperableKernelFilterDelegate job = SeparableKernelFilter.Schedule;

        public KernelFilterType filter;
        [Range(1, 32)]
        public int iterations = 1;
        private NativeArray<float> tmp;

        public override void ResizeNativeContainers(int size){
            // Resize containers
            dataLength = size;
            if(tmp.IsCreated){
                tmp.Dispose();
            }
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
            JobHandle[] handles = new JobHandle[iterations];
            for (int i = 0; i < iterations; i++){
                if (i == 0){
                    handles[i] = job(d.data, tmp, filter, d.resolution, dependency);
                }else{
                    handles[i] = job(d.data, tmp, filter, d.resolution, handles[i - 1]);
                }
            }
            jobHandle = handles[iterations - 1];
        }

        public override void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
            }
        }
    }
}