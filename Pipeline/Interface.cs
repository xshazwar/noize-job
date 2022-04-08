using System;

using Unity.Jobs;

namespace xshazwar.noize.pipeline {
    
    public interface IPipeline : IScheduleJobCallback {}
    
    public interface IStage : IScheduleStage, IStageBroadcaster, IJobTarget {}

    public interface IScheduleStage {
        public void Schedule(StageIO requirements);
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
        public void ReceiveHandledInput(StageIO inputData, JobHandle dependency);
    }

    // public interface IJobBroadcaster{
    //     public Action<StageIO>OnJobCompleteAction {get; set;}
    // }

    public interface IStageBroadcaster{
        // public Action<StageIO>OnStageCompleteAction {get; set;}
        public Action<StageIO, JobHandle>OnStageScheduledAction {get; set;}
    }
}