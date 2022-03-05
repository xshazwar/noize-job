using System;

using Unity.Collections;
using Unity.Jobs;

using UnityEngine;

namespace xshazwar.noize.pipeline {
    
    public abstract class PipelineStage : ScriptableObject, IStage {

        protected JobHandle jobHandle;

        bool stageQueued;
        bool stageTriggered;

        StageIO inputData;
        StageIO inputOverride;
        StageIO outputData;
        public Action<StageIO> OnJobComplete {get; set;}

        public void OnEnable(){
            stageQueued = false;
            stageTriggered = false;
        }
        public abstract void Schedule( StageIO requirements );
        // Schedule job with input data.
        // Setup output data
        // Set job handle
        public void SetOverride(StageIO inputOverride){
            this.inputOverride = inputOverride;
        }
        
        public void ReceiveInput(StageIO inputData){
            Debug.Log($"Stage: {this.GetType().ToString()} got input of type {inputData.GetType().ToString()}");
            this.inputData = inputData;
            inputOverride?.ImposeOn(ref this.inputData);
            stageQueued = true;
        }
        // Set input data

        public void OnUpdate(){
            if (stageTriggered){
                if (!jobHandle.IsCompleted){
                    return;
                }
                stageTriggered = false;
                stageQueued = false;
                jobHandle.Complete();
                outputData = inputData; // TODO put this somewhere in the subclass?
                OnJobComplete?.Invoke(outputData);
            }
            
            if (stageQueued && !stageTriggered){
                stageTriggered = true;
                Schedule(inputData);
                stageQueued = false;
            }
        }
        public virtual void OnDestroy(){}
    }
}