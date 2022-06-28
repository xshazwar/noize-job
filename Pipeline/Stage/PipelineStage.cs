using System;

using Unity.Collections;
using Unity.Jobs;

using UnityEngine;

namespace xshazwar.noize.pipeline {
    
    public abstract class PipelineStage : ScriptableObject, IStage {

        protected JobHandle jobHandle;
        StageIO inputData;
        protected StageIO outputData;
        public Action<StageIO, JobHandle>OnStageScheduledAction {get; set;}

        public void OnEnable(){
        }
        public abstract void Schedule( StageIO requirements, JobHandle dependency );
        // Schedule job with input data.
        // Setup output data
        // Set job handle
        
        public void ReceiveHandledInput(StageIO inputData, JobHandle dependency){
            Schedule(inputData, dependency);
            TransformData(inputData);
            OnStageScheduled(outputData, jobHandle);
        }

        public void Destroy(){
            OnDestroy();
        }

        public virtual void OnStageScheduled(StageIO res, JobHandle dependency){
            OnStageScheduledAction?.Invoke(res, jobHandle);
        }

        public virtual void TransformData(StageIO inputData){
            outputData = inputData;
        }
        public virtual void OnStageComplete(){
        }
        public virtual void OnDestroy(){}
    }
}