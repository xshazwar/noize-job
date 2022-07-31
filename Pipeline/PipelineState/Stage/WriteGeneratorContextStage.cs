using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "WriteGeneratorContextStage", menuName = "Noize/State/WriteContext", order = 2)]
    public class WriteGeneratorContextStage: PipelineStage {

        static FlushWriteSliceDelegate job = FlushWriteSlice.Schedule;
        static LockJobDelegate second = LockJob.Schedule;
        public string contextAlias;
        private string getBufferName(GeneratorData d){
            return $"{d.xpos}_{d.zpos}__{d.resolution}__{contextAlias}";
        }
        public override bool IsSchedulable(PipelineWorkItem job){
            if(job.stageManager == null){
                return false;
            }
            if(job.stageManager.IsLocked<NativeArray<float>>(getBufferName((GeneratorData)job.data))){
                return false;
            }
            return true;
        }
        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData gd = (GeneratorData) requirements.data;
            int res = gd.resolution * gd.resolution;
            string bufferName = getBufferName(gd);
            NativeArray<float> buffer = requirements.stageManager.GetBuffer<float, NativeArray<float>>(bufferName, res);
            NativeSlice<float> contextTarget = new NativeSlice<float>(buffer);
            
            JobHandle h1 = job(
                contextTarget,
                requirements.data.data,
                dependency
            );
            jobHandle = second(h1);
            requirements.stageManager.TrySetLock<NativeArray<float>>(bufferName, h1, jobHandle);
        }
    }
}