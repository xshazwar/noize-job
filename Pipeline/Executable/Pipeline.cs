using System;
using System.Collections;
using System.Collections.Generic;

using xshazwar.unity;
#if UNITY_EDITOR
using xshazwar.unity.editor;
#endif

using UnityEngine;

namespace xshazwar.noize.pipeline {
    public abstract class BasePipeline : MonoBehaviour, IPipeline
    {
        bool pipelineQueued;
        bool pipelineRunning;
        StageIO pipelineInput;
        StageIO pipelineOutput;
        
        [SerializeField]
        private List<PipelineStage> stages;
        public Action<StageIO> OnJobComplete {get; set;}

        public void Start(){
            BeforeStart();
            pipelineQueued = false;
            pipelineRunning = false;
            // don't bother with update until it's been scheduled the first time
            // enabled = false;
            Setup();
            AfterStart();
        }

        public abstract void BeforeStart();
        public abstract void AfterStart();

        public void Schedule(StageIO requirements, Action<StageIO> onResult){
            if (stages == null){
                throw new Exception("No stages in pipeline");
            }
            enabled = true;
            pipelineInput = requirements;
            
            OnJobComplete += (StageIO res) => { onResult?.Invoke(res);};
            pipelineQueued = true;
        }

        public void OnFinalStageComplete(StageIO res){
            pipelineRunning = false;
            pipelineOutput = res;
            OnJobComplete?.Invoke(pipelineOutput);
            OnJobComplete = null;
            OnPipelineComplete();
        }

        public abstract void OnPipelineComplete();
        // Cleanup, etc

        public void Setup(){
            PipelineStage previousStage = null;
            foreach(PipelineStage stage in stages){
                if(previousStage != null){
                    previousStage.OnJobComplete += stage.ReceiveInput;
                }
                previousStage = stage;
            }
            stages[stages.Count - 1].OnJobComplete += OnFinalStageComplete;
        }
        
        public abstract void BeforeUpdate();
        // Anything to be done before scheduling
        public abstract void AfterUpdate();
        // Anything to be done before scheduling

        public void Update(){
            BeforeUpdate();
            OnUpdate();
            AfterUpdate();
        }

        public void OnUpdate(){
            if (pipelineRunning){
                foreach(PipelineStage stage in stages){
                    stage.OnUpdate();
                }
            }
            
            if (pipelineQueued && !pipelineRunning){
                pipelineRunning = true;
                stages[0].ReceiveInput(pipelineInput);
                pipelineQueued = false;
            }

        }

    }
}