using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;

using xshazwar.noize;
#if UNITY_EDITOR
using static System.Diagnostics.Stopwatch;
using xshazwar.noize.editor;
#endif

using UnityEngine;
using Unity.Profiling;

namespace xshazwar.noize.pipeline {

    public  class PipelineWorkItem {
        // Used for internal handling of work queues in pipelines
        public StageIO data;
        public Action<StageIO> action;
    }
    public abstract class BasePipeline : MonoBehaviour, IPipeline
    {
        protected bool pipelineQueued;
        protected bool pipelineRunning;
        protected StageIO pipelineInput;
        protected StageIO pipelineOutput;

        #if UNITY_EDITOR
        protected System.Diagnostics.Stopwatch wall;
        #endif
        public string alias = "Unnamed Pipeline";

        protected ConcurrentQueue<PipelineWorkItem> queue;
        
        // This is a list of stages, but references the type and must be instantiated
        [SerializeField]
        public List<PipelineStage> stages;

        protected List<PipelineStage> stage_instances;

        public Action<StageIO> OnJobCompleteAction {get; set;}

        public void Start(){
            BeforeStart();
            queue = new ConcurrentQueue<PipelineWorkItem>();
            pipelineQueued = false;
            pipelineRunning = false;
            // don't bother with update until it's been scheduled the first time
            // enabled = false;
            Setup();
            AfterStart();
        }

        public virtual BasePipeline[] GetDependencies(){
            return new BasePipeline[]{ this };
        }

        void OnValidate(){
            this.name = $"Pipeline:{alias}";
        }

        public void Enqueue(StageIO input, Action<StageIO> action){
            queue.Enqueue(new PipelineWorkItem {
                data = input,
                action = action
            });
        }

        public void Schedule(StageIO requirements, Action<StageIO> onResult){
            // Synchronous Scheduling
            if (stages == null){
                throw new Exception("No stages in pipeline");
            }
            enabled = true;
            
            #if UNITY_EDITOR
            wall = System.Diagnostics.Stopwatch.StartNew();
            #endif
            
            pipelineInput = requirements;
            
           OnJobCompleteAction += (StageIO res) => { 
               Debug.Log($"PL complete {res.uuid}");
               onResult?.Invoke(res);
            };
            pipelineQueued = true;
        }

        public void OnFinalStageComplete(StageIO res){
            #if UNITY_EDITOR
            wall.Stop();
            Debug.LogWarning($"{alias} -> {res.uuid}: {wall.ElapsedMilliseconds}ms");
            #endif
            pipelineRunning = false;
            pipelineOutput = res;
            OnJobCompleteAction?.Invoke(pipelineOutput);
            OnJobCompleteAction = null;
            OnPipelineComplete();
        }

        public void Setup(){
            stage_instances = new List<PipelineStage>();
            foreach(PipelineStage stage in stages){
                stage_instances.Add(UnityEngine.Object.Instantiate(stage));
            }
            PipelineStage previousStage = null;
            foreach(PipelineStage stage in stage_instances){
                if(previousStage != null){
                    previousStage.OnStageCompleteAction += stage.ReceiveInput;
                }
                previousStage = stage;
            }
            stage_instances[stages.Count - 1].OnStageCompleteAction += OnFinalStageComplete;
            Debug.Log("Pipeline Setup Complete");
        }

        public void Update(){
            BeforeUpdate();
            OnUpdate();
            AfterUpdate();
        }

        public virtual void OnUpdate(){
            if (pipelineRunning){
                foreach(PipelineStage stage in stage_instances){
                    stage.OnUpdate();
                }
            }else if (!pipelineRunning && !pipelineQueued){
                if (queue.Count > 0){
                    PipelineWorkItem wi;
                    if (queue.TryDequeue(out wi)){
                        Schedule(wi.data, wi.action);
                    }
                }
            }else if (pipelineQueued && !pipelineRunning){
                pipelineRunning = true;
                stage_instances[0].ReceiveInput(pipelineInput);
                pipelineQueued = false;
            }
        }

        protected void CleanUpStages(){
            foreach(PipelineStage stage in stage_instances){
                try{
                    stage.OnDestroy();
                }catch(Exception err){
                    Debug.LogError(err);
                }
            }
        }

        public virtual void OnDestroy()
        {
            CleanUpStages();
        }
        
        // Lifecycle Hooks
        protected virtual void BeforeStart(){}
        protected virtual void AfterStart(){}
        protected virtual void OnPipelineComplete(){}
        // Cleanup, etc
        protected virtual void BeforeUpdate(){}
        // Anything to be done before scheduling
        protected virtual void AfterUpdate(){}
        // Anything to be done before scheduling
    }
}