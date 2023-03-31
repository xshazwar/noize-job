using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter.blur {

    [CreateAssetMenu(fileName = "StageSmoothBlur", menuName = "Noize/Filter/Blur/SmoothBlurFilter", order = 2)]
    public class StageSmoothBlur: PipelineStage {

        static SmoothFilter.SmoothFilterDelegate job = SmoothFilter.Schedule;

        [Range(1, 32)]
        public int iterations = 1;
        [Range(3, 25)]
        public int width = 1;
        private NativeArray<float> tmp;

        public override void ResizeNativeContainers(int size){
            // Resize containers
            
            if(tmp.IsCreated){
                tmp.Dispose();
            }
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
            int width_ = BlurHelper.limitWidth(width);
            JobHandle[] handles = new JobHandle[iterations];
            for (int i = 0; i < iterations; i++){
                if (i == 0){
                    handles[i] = job(d.data, tmp, width_, d.resolution, dependency);
                }else{
                    handles[i] = job(d.data, tmp, width_, d.resolution, handles[i - 1]);
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