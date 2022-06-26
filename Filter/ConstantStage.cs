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
        private int dataLength = 0;
        private NativeArray<float> tmp;
        public override void Schedule( StageIO req, JobHandle dep){
            if (req is GeneratorData){
                GeneratorData d = (GeneratorData) req;
                // This could take a while, so we'll use Persistent. May want to logic this a bit
                if(d.data.Length != dataLength){
                    dataLength = d.data.Length;
                    if(tmp.IsCreated){
                        tmp.Dispose();
                    }
                    tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    dataLength = d.data.Length;
                }
                jobHandle = jobs[(int)operation](
                    d.data,
                    tmp,
                    value,
                    d.resolution,
                    dep
                );
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