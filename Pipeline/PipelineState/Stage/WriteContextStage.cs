using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "WriteContext", menuName = "Noize/State/WriteContext", order = 2)]
    public class WriteContext: PipelineStage, IModifyPipelineBufferContext {

        static FlushWriteSliceDelegate job = FlushWriteSlice.Schedule;
        public int bufferIndex {get; set;}
        private string contextAlias;
        public PipelineBufferOperation bufferOperation {get { return PipelineBufferOperation.WRITE;}}
        public void SetBufferContext(string alias){
            contextAlias = alias;
        }
        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency){
            CheckRequirements<GeneratorData>(requirements);
            NativeSlice<float> contextTarget = requirements.sharedContext[contextAlias];
            jobHandle = job(
                requirements.data.data,
                contextTarget,
                dependency
            );
        }
    }
}