using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "ReadContext", menuName = "Noize/State/ReadContext", order = 2)]
    public class ReadContext: PipelineStage, IModifyPipelineBufferContext {

        static FlushWriteSliceDelegate job = FlushWriteSlice.Schedule;
        public int bufferIndex {get; set;}
        private string contextAlias;
        public PipelineBufferOperation bufferOperation {get { return PipelineBufferOperation.READ;}}
        public void SetBufferContext(string alias){
            contextAlias = alias;
        }
        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency){
            CheckRequirements<GeneratorData>(requirements);
            NativeSlice<float> contextTarget = requirements.sharedContext[contextAlias];
            jobHandle = job(
                contextTarget,
                requirements.data.data,
                dependency
            );
        }
    }
}