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
        public void ReceiveInput(StageIO inputData){
            this.inputData = inputData;
            stageQueued = true;
        }
        // Set input data

        public void OnUpdate(){
            if (stageTriggered){
                if (!jobHandle.IsCompleted){
                    return;
                }
                jobHandle.Complete();
                OnJobComplete?.Invoke(outputData);
                stageTriggered = false;
            }
            
            if (stageQueued && !stageTriggered){
                stageTriggered = true;
                Schedule(inputData);
                stageQueued = false;
            }
        }
    }
}