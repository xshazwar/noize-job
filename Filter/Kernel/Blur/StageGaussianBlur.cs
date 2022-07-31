using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter.blur {

    [CreateAssetMenu(fileName = "StageGaussianBlur", menuName = "Noize/Filter/Blur/GaussianBlurFilter", order = 2)]
    public class StageGaussianBlur: PipelineStage {

        static GaussFilter.GaussFilterDelegate job = GaussFilter.Schedule;

        [Range(1, 32)]
        public int iterations = 1;
        public GaussSigma sigma;
        [Range(3, 25)]
        public int width = 3;
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
            JobHandle[] handles = new JobHandle[iterations];
            int width_ = BlurHelper.limitWidth(width);
            for (int i = 0; i < iterations; i++){
                if (i == 0){
                    handles[i] = job(d.data, tmp, width_, sigma, d.resolution, dependency);
                }else{
                    handles[i] = job(d.data, tmp, width_, sigma, d.resolution, handles[i - 1]);
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