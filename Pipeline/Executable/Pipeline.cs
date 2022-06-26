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
using UnityEngine.Profiling;
using Unity.Jobs;

namespace xshazwar.noize.pipeline {

    public class PipelineWorkItem {
        // Used for internal handling of work queues in pipelines
        public StageIO data;
        public StageIO outputData;
        public Action<StageIO> completeAction;
        public Action<StageIO, JobHandle> scheduledAction;
        public JobHandle dependency;
    }
    public abstract class BasePipeline : MonoBehaviour, IPipeline
    {
        protected bool pipelineBeingScheduled;
        protected bool pipelineRunning;

        #if UNITY_EDITOR
            protected System.Diagnostics.Stopwatch wall;
            public List<bool> stageActive;

        #endif
        public string alias = "Unnamed Pipeline";

        protected ConcurrentQueue<PipelineWorkItem> queue;
        protected PipelineWorkItem activeItem;
        
        protected JobHandle pipelineHandle;
        
        // This is a list of stages, but references the type and must be instantiated
        [SerializeField]
        public List<PipelineStage> stages;

        protected List<PipelineStage> stage_instances;

        public void Start(){
            BeforeStart();
            queue = new ConcurrentQueue<PipelineWorkItem>();
            pipelineBeingScheduled = false;
            pipelineRunning = false;
            // don't bother with update until it's been scheduled the first time
            // enabled = false;
            Setup();
            AfterStart();
        }

        public virtual BasePipeline[] GetDependencies(){
            return new BasePipeline[]{ this };
        }

        void SetupDefaultActiveStages(){
        #if UNITY_EDITOR
            if(stageActive == null || stageActive.Count != stages.Count){
                stageActive = new List<bool>();
                for (int i = 0; i < stages.Count; i++){
                    stageActive.Add(true);
                }
            }
        #endif
        }

        void OnValidate(){
            this.name = $"Pipeline:{alias}";
        #if UNITY_EDITOR
            SetupDefaultActiveStages();
        #endif
        }

        public void Enqueue(
            StageIO input,
            Action<StageIO, JobHandle> scheduleAction = null,
            Action<StageIO> completeAction = null,
            JobHandle dependency = default(JobHandle)
        ){

            queue.Enqueue(new PipelineWorkItem {
                data = input,
                scheduledAction = scheduleAction,
                completeAction = completeAction,
                dependency = dependency
            });
        }
        public void Schedule(StageIO input,
                Action<StageIO, JobHandle> scheduleAction = null,
                Action<StageIO> completeAction = null,
                JobHandle dependency = default(JobHandle)){
            Schedule(new PipelineWorkItem {
                data = input,
                scheduledAction = scheduleAction,
                completeAction = completeAction,
                dependency = dependency
            });
        }
        public void Schedule(PipelineWorkItem wi){
            activeItem = wi;
            Schedule();
        }

        public void Schedule(){
            if (stages == null){
                throw new Exception("No stages in pipeline");
            }
            enabled = true;
            pipelineBeingScheduled = true;
            #if UNITY_EDITOR
            wall = System.Diagnostics.Stopwatch.StartNew();
            #endif
            stage_instances[0].ReceiveHandledInput(activeItem.data, activeItem.dependency);
            OnPipelineSchedule(activeItem.data, activeItem.completeAction);
        }

        public void OnPipelineFullyScheduled(StageIO res, JobHandle handle){
            pipelineHandle = handle;
            pipelineRunning = true;
            pipelineBeingScheduled = false;
            activeItem.outputData = res;
            Debug.LogWarning($"{alias} fully scheduled {res.uuid} in ({wall.ElapsedMilliseconds}ms)");
            activeItem.scheduledAction?.Invoke(res, handle);
        }

        // public void OnFinalStageComplete(StageIO res){
        //     #if UNITY_EDITOR
        //     wall.Stop();
        //     Debug.LogWarning($"{alias} -> {res.uuid}: {wall.ElapsedMilliseconds}ms");
        //     #endif
        //     pipelineRunning = false;
        //     pipelineOutput = res;
        //     OnJobCompleteAction?.Invoke(pipelineOutput);
        //     OnJobCompleteAction = null;
        //     OnPipelineComplete(res);
        // }
        public void Setup(){
            stage_instances = new List<PipelineStage>();
            for (int i = 0; i < stages.Count; i++){
                #if UNITY_EDITOR
                SetupDefaultActiveStages();
                if(stageActive[i] == true){
                    stage_instances.Add(UnityEngine.Object.Instantiate(stages[i]));
                }
                #else
                stage_instances.Add(UnityEngine.Object.Instantiate(stages[i]));
                #endif
                
            }
            // foreach(PipelineStage stage in stages){
            //     stage_instances.Add(UnityEngine.Object.Instantiate(stage));
            // }
            PipelineStage previousStage = null;
            foreach(PipelineStage stage in stage_instances){
                if(previousStage != null){
                    previousStage.OnStageScheduledAction += stage.ReceiveHandledInput;
                }
                previousStage = stage;
            }
            stage_instances[stage_instances.Count - 1].OnStageScheduledAction += OnPipelineFullyScheduled;
            Debug.Log($"Pipeline {alias} : Setup Complete");
        }


        public void Update(){
            BeforeUpdate();
            OnUpdate();
            AfterUpdate();
        }

        public void LateUpdate(){
            OnLateUpdate();
            if (pipelineRunning && pipelineHandle.IsCompleted){
                UnityEngine.Profiling.Profiler.BeginSample("JobHandle.Complete()");
                pipelineHandle.Complete();
                UnityEngine.Profiling.Profiler.EndSample();
                UnityEngine.Profiling.Profiler.BeginSample("CleanUp");
                CleanUp();
                UnityEngine.Profiling.Profiler.EndSample();
                #if UNITY_EDITOR
                wall.Stop();
                Debug.LogWarning($"{alias} completed -> {activeItem.outputData.uuid}: {wall.ElapsedMilliseconds}ms");
                #endif
                UnityEngine.Profiling.Profiler.BeginSample("InvokeCustomCallback");
                activeItem.completeAction?.Invoke(activeItem.outputData);
                UnityEngine.Profiling.Profiler.EndSample();
                UnityEngine.Profiling.Profiler.BeginSample("PipelineComplete");
                OnPipelineComplete(activeItem.outputData);
                pipelineRunning = false;
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        public virtual void OnUpdate(){
            if (!pipelineRunning && !pipelineBeingScheduled){
                if (queue.Count > 0){
                    PipelineWorkItem wi;
                    if (queue.TryDequeue(out wi)){
                        Debug.LogWarning($"{alias} servicing {wi.data.uuid} | {queue.Count} remaining");
                        Schedule(wi);
                    }
                }
            }else if (!pipelineRunning && queue.Count > 0){
                Debug.Log($"{alias} has life of {queue.Count} objects in queue for {pipelineBeingScheduled}");
            }
        }

        public virtual void CleanUp(){
            foreach(PipelineStage stage in stage_instances){
                try{
                    stage.OnStageComplete();
                }catch(Exception err){
                    Debug.LogError(err);
                }
            }
        }

        public virtual void OnLateUpdate(){

        }

        protected void DestroyStages(){
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
            DestroyStages();
        }
        
        // Lifecycle Hooks
        protected virtual void BeforeStart(){}
        protected virtual void AfterStart(){}

        protected virtual void OnPipelineSchedule(StageIO requirements, Action<StageIO> onResult){}
        protected virtual void OnPipelineComplete(StageIO res){}
        // Cleanup, etc
        protected virtual void BeforeUpdate(){}
        // Anything to be done before scheduling
        protected virtual void AfterUpdate(){}
        // Anything to be done before scheduling
    }
}