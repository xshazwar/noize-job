using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "Constant", menuName = "Noize/Filter/Constant", order = 2)]
    public class ConstantStage: PipelineStage {

        public enum ConstantOperationType {
            MULTIPLY,
            BINARIZE
        }

        static ConstantJobScheduleDelegate[] jobs = new ConstantJobScheduleDelegate[] {
            ConstantJob<ConstantMultiply, RWTileData>.ScheduleParallel,
            ConstantJob<ConstantBinarize, RWTileData>.ScheduleParallel
        };

        public ConstantOperationType operation;
        [Range(0, 1)]
        public float value = 0.5f;
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
            jobHandle = jobs[(int)operation](
                d.data,
                tmp,
                value,
                d.resolution,
                dependency
            );
        }

        public override void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
            }
        }
    }
}