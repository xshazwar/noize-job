using System;

namespace xshazwar.noize.pipeline {
    
    public interface IPipeline : IScheduleJobCallback, IJobBroadcaster {}
    
    public interface IStage : IScheduleJob, IJobBroadcaster, IJobTarget {}

    public interface IScheduleJob {
        public void Schedule(StageIO requirements);
        public void OnUpdate();
    }

    public interface IScheduleJobCallback{
        
        public void Schedule(StageIO requirements, Action<StageIO> onResult);
        public void OnUpdate();

    }

    public interface IJobTarget {
        public void ReceiveInput(StageIO data);
    }

    public interface IJobBroadcaster{
        public Action<StageIO> OnJobComplete {get; set;}
    }
}