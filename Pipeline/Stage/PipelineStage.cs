using System;

using Unity.Collections;
using Unity.Jobs;

using UnityEngine;

namespace xshazwar.noize.pipeline {
    
    public abstract class PipelineStage : ScriptableObject, IStage {

        protected JobHandle jobHandle;
        public Action<PipelineWorkItem, JobHandle>OnStageScheduledAction {get; set;}
        protected bool arraysInitialized = false;
        protected int dataLength = 0;

        public void OnEnable(){
        }
        public virtual void ResizeNativeContainers(int size){
            // Resize containers
        }
        
        public virtual void CheckRequirements<T>(PipelineWorkItem requirements) where T: StageIO {
            if (requirements.data is T){
                T d = (T) requirements.data;
                if (d.data.Length != this.dataLength){
                    this.dataLength = d.data.Length;
                    ResizeNativeContainers(d.data.Length);
                }
            }else{
                throw new Exception($"Unhandled stageio {requirements.data.GetType().ToString()}");
            }
        }

        public virtual void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
        }

        public void ReceiveHandledInput(PipelineWorkItem requirements, JobHandle dependency){
            Schedule(requirements, dependency);
            TransformData(requirements);
            OnStageScheduled(requirements, jobHandle);
        }

        public void Destroy(){
            OnDestroy();
        }

        public virtual void TransformData(PipelineWorkItem data){
        }

        public virtual void OnStageScheduled(PipelineWorkItem requirements, JobHandle dependency){
            OnStageScheduledAction?.Invoke(requirements, jobHandle);
        }
        public virtual void OnStageComplete(){
        }
        public virtual void OnDestroy(){}
    }
}