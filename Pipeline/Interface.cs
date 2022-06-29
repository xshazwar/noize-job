using System;

using Unity.Jobs;

namespace xshazwar.noize.pipeline {
    
    public interface IPipeline : IScheduleJobCallback {}
    
    public interface IStage : IScheduleStage, IStageBroadcaster, IJobTarget {}

    public interface IScheduleStage {
        public void Schedule(PipelineWorkItem requirements, JobHandle dependency);
        // public void OnUpdate();
    }

    public interface IScheduleJobCallback{
        
        public void Enqueue(
            StageIO input,
            Action<StageIO, JobHandle> scheduleAction = null,
            Action<StageIO> completeAction = null,
            JobHandle dependency = default(JobHandle)
        );
        public void Schedule(StageIO input,
                Action<StageIO, JobHandle> scheduleAction = null,
                Action<StageIO> completeAction = null,
                JobHandle dependency = default(JobHandle));
        // public void OnUpdate();

    }

    public interface IJobTarget {
        public void ReceiveHandledInput(PipelineWorkItem inputData, JobHandle dependency);
    }

    public interface IStageBroadcaster{
        // public Action<StageIO>OnStageCompleteAction {get; set;}
        public Action<PipelineWorkItem, JobHandle>OnStageScheduledAction {get; set;}
    }
}