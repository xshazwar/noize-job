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
        private int dataLength = 0;
        private NativeArray<float> tmp;
        public override void Schedule( StageIO req, JobHandle dep){
            if (req is GeneratorData){
                GeneratorData d = (GeneratorData) req;
                UnityEngine.Profiling.Profiler.BeginSample("Allocate tmp");
                // This could take a while, so we'll use Persistent. May want to logic this a bit
                if(d.data.Length != dataLength){
                    dataLength = d.data.Length;
                    if(tmp.IsCreated){
                        tmp.Dispose();
                    }
                    tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
                UnityEngine.Profiling.Profiler.EndSample();
                JobHandle[] handles = new JobHandle[iterations];
                for (int i = 0; i < iterations; i++){
                    UnityEngine.Profiling.Profiler.BeginSample("Enqueue Step");
                    if (i == 0){
                        handles[i] = job(d.data, tmp, filter, d.resolution, dep);
                    }else{
                        handles[i] = job(d.data, tmp, filter, d.resolution, handles[i - 1]);
                    }
                    UnityEngine.Profiling.Profiler.EndSample();
                }
                jobHandle = handles[iterations - 1];
            }
            else{
                throw new Exception($"Unhandled stageio {req.GetType().ToString()}");
            }
            Debug.Log("scheduled");
        }

        public override void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
            }
        }
    }
}