using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "WriteContext", menuName = "Noize/State/WriteContext", order = 2)]
    public class WriteContext: PipelineStage {

        static FlushWriteSliceDelegate job = FlushWriteSlice.Schedule;

        public int bufferIndex = 0;
        private NativeArray<float> tmp;
        private string contextAlias;
        public override void ResizeNativeContainers(int size){
            if(tmp.IsCreated){
                tmp.Dispose();
            }
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            // Resize containers
            dataLength = size;
        }
        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency){
            CheckRequirements<GeneratorData>(requirements);
            // jobHandle = job(
            //     d.data,
            //     contextTarget,
            //     dep
            // );
        }

        public override void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
            }
        }
    }
}