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
    public class PipelineWorkItem {
        // Used for internal handling of work queues in pipelines
        public StageIO data;
        public Action<StageIO> completeAction;
        public Action<StageIO, JobHandle> scheduledAction;
        public JobHandle dependency;
        public Dictionary<string, NativeSlice<float>> sharedContext;
    }

    [System.Serializable] 
    public class MaskedPipeline {
        
        [SerializeField]
        public List<string> ReadBuffers;
        [SerializeField]
        public List<string> WriteBuffers;
        
        [SerializeField]
        public StageMask mask;
        [SerializeField]
        public PipelineDefinition pipeline;

        public void Validate(){
            if(pipeline != null){
                if (mask != null && mask.maskSize == pipeline.stages.Count){
                    return;
                }else {
                    mask = new StageMask(pipeline);
                }
            }
        }

        public List<PipelineStage> GetStages(){
            return pipeline.GetStages(mask);
        }

        public List<string> GetRequiredReadBuffers(){
            return null;
        }
        public List<string> GetRequiredWriteBuffers(){
            return null;
        }
    }
    
    [System.Serializable] 
    public class StageMask {
        
        [SerializeField]
        public List<bool> activity;

        public int maskSize {
            get { return activity?.Count != null ? activity.Count : 0 ;}
            private set {}
        }

        public StageMask(PipelineDefinition input){
            UpdateMaskSize(input);
        }

        public void UpdateMaskSize(PipelineDefinition input){
            UpdateMaskSize(input.stages.Count);
        }

        public void UpdateMaskSize(int count){
            activity = new List<bool>();
            for (int i = 0; i < count; i++){
                activity.Add(true);
            }
        }
    }

    [CreateAssetMenu(fileName = "PipelineDefinition", menuName = "Noize/PipelineDefinition", order = 2)]
    public class PipelineDefinition : ScriptableObject {
        
        [SerializeField]
        public List<PipelineStage> stages;

        public IEnumerable<PipelineStage> GetMaskedStages(StageMask mask = null){
            for (int i = 0; i < stages.Count; i++){
                if (mask != null && mask.maskSize > i){
                    if (mask.activity[i] == true){
                        yield return stages[i];
                    }
                }else{
                    yield return stages[i];
                }
            }
        }

        public List<PipelineStage> GetStages(StageMask mask = null){
            List<PipelineStage> res = new List<PipelineStage>();
            Debug.LogWarning($"{this.ToString()} is creating new stage instances");
            foreach(PipelineStage stage in GetMaskedStages(mask)){
                res.Add(UnityEngine.Object.Instantiate(stage));
            }
            return res;
        }
    }

}