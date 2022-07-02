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
using Unity.Collections;

namespace xshazwar.noize.pipeline {

    public abstract class BasePipeline : MonoBehaviour, IPipeline
    {
        protected bool pipelineBeingScheduled;
        protected bool pipelineRunning;

        #if UNITY_EDITOR
            protected System.Diagnostics.Stopwatch wall;
        #endif
        public string alias = "Unnamed Pipeline";

        // This is only concurrent for puts, all gets are from the main thread
        protected ConcurrentQueue<PipelineWorkItem> queue;
        // If the top of the queue has unmet deps, then we stick it here to be processed first
        protected List<PipelineWorkItem> dependencyHell;
        protected PipelineWorkItem activeItem;
        protected JobHandle pipelineHandle;
        
        [SerializeField]
        public List<MaskedPipeline> pipes;

        protected List<PipelineStage> stage_instances;

        // List of buffer aliases that this pipeline expects to read, from the pipeline definition
        protected List<string> contextRequirements = null;
        protected PipelineStateManager contextManager;

        public bool pipeLineReady { get; private set;}

        public void OnEnable(){
            BeforeStart();
            dependencyHell = new List<PipelineWorkItem>();
            queue = new ConcurrentQueue<PipelineWorkItem>();
            pipelineBeingScheduled = false;
            pipelineRunning = false;
            // don't bother with update until it's been scheduled the first time
            // enabled = false;
            Setup();
            AfterStart();
            pipeLineReady = true;
        }

        public virtual BasePipeline[] GetDependencies(){
            return new BasePipeline[]{ this };
        }

        void OnValidate(){
            this.name = $"Pipeline:{alias}";
            if (pipes != null){
                foreach( MaskedPipeline mpl in pipes){
                    mpl.Validate();
                }
            }
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
            if (pipes == null){
                throw new Exception("No stages in pipeline");
            }
            enabled = true;
            pipelineBeingScheduled = true;
            #if UNITY_EDITOR
            wall = System.Diagnostics.Stopwatch.StartNew();
            #endif
            stage_instances[0].ReceiveHandledInput(activeItem, activeItem.dependency);
            OnPipelineSchedule(activeItem.data, activeItem.completeAction);
        }

        public void OnPipelineFullyScheduled(PipelineWorkItem res, JobHandle handle){
            pipelineHandle = handle;
            pipelineRunning = true;
            pipelineBeingScheduled = false;
            Debug.LogWarning($"{alias} fully scheduled {res.data.uuid} in ({wall.ElapsedMilliseconds}ms)");
            activeItem.scheduledAction?.Invoke(res.data, handle);
        }

        public void Setup(){
            stage_instances = new List<PipelineStage>();
            if (pipes == null){
                return;
            }
            foreach (MaskedPipeline mpl in pipes){
                foreach(PipelineStage stage in mpl.GetStages()){
                    stage_instances.Add(stage);
                }
            }
            PipelineStage previousStage = null;
            foreach(PipelineStage stage in stage_instances){
                if(previousStage != null){
                    previousStage.OnStageScheduledAction += stage.ReceiveHandledInput;
                }
                previousStage = stage;
            }
            if (stage_instances.Count > 0){
                stage_instances[stage_instances.Count - 1].OnStageScheduledAction += OnPipelineFullyScheduled;
            }
            Debug.LogWarning($"Pipeline {alias} : Setup Complete -> {stage_instances.Count}");
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
                Debug.LogWarning($"{alias} completed -> {activeItem.data.uuid}: {wall.ElapsedMilliseconds}ms");
                #endif
                UnityEngine.Profiling.Profiler.BeginSample("InvokeCustomCallback");
                activeItem.completeAction?.Invoke(activeItem.data);
                UnityEngine.Profiling.Profiler.EndSample();
                UnityEngine.Profiling.Profiler.BeginSample("PipelineComplete");
                OnPipelineComplete(activeItem.data);
                pipelineRunning = false;
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        public PipelineWorkItem? GetNextJob(){
            // grab a snapshot of dep-hell so we can mutate it in resolveorshelve
            PipelineWorkItem[] inHell = dependencyHell.ToArray();
            for(int i = 0; i < inHell.Length; i++){
                if (ResolveOrShelve(ref inHell[i])){
                    return inHell[i];
                }
            }
            if (queue.Count > 0){
                PipelineWorkItem wi;
                while(queue.TryDequeue(out wi)){
                    if (ResolveOrShelve(ref wi)){
                        return wi;
                    }
                }
            }
            return null;
        }

        public bool ResolveOrShelve(ref PipelineWorkItem job){
            if (contextRequirements == null){
                return true;
            }
            if (!TryHydrateSharedContext(job)){
                dependencyHell.Add(job);
                return false;
            }else{
                return true;
            } 
        }

        public void ServiceQueue(){         
            PipelineWorkItem job = GetNextJob();
            if (job != null){
                Debug.LogWarning($"{alias} servicing {job.data.uuid} | {queue.Count} remaining");
                Schedule(job);
            }
        }

        public virtual void OnUpdate(){
            if (!pipelineRunning && !pipelineBeingScheduled){
                ServiceQueue();
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

        public bool TryHydrateSharedContext(PipelineWorkItem item){
            return true;
        }

        public void OnDisable(){
            Destroy();
        }

        public virtual void Destroy()
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