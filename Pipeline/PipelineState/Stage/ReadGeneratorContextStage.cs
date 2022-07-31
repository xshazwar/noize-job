using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "ReadGeneratorContextStage", menuName = "Noize/State/ReadContext", order = 2)]
    public class ReadGeneratorContextStage: PipelineStage {

        static FlushWriteSliceDelegate job = FlushWriteSlice.Schedule;
        public string contextAlias;

        private string getBufferName(GeneratorData d){
            return $"{d.xpos}_{d.zpos}__{d.resolution}__{contextAlias}";
        }
        public override bool IsSchedulable(PipelineWorkItem job){
            if(job.stageManager == null){
                return false;
            }
            GeneratorData gd = (GeneratorData) job.data;
            int res = gd.resolution * gd.resolution;
            string bufferName = getBufferName(gd);
            if(!job.stageManager.BufferExists<NativeArray<float>>(bufferName)){
                return false;
            }
            return !job.stageManager.IsLocked<NativeArray<float>>(bufferName);
            
        }
        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData gd = (GeneratorData) requirements.data;
            int res = gd.resolution * gd.resolution;
            NativeArray<float> buffer = requirements.stageManager.GetBuffer<float, NativeArray<float>>(getBufferName(gd), res);
            NativeSlice<float> contextTarget = new NativeSlice<float>(buffer);
            jobHandle = job(
                requirements.data.data,
                contextTarget,
                dependency
            );
        }
    }
}