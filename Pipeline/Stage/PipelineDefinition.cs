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

    [System.Serializable] 
    public class MaskedPipeline {
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

        public List<PipelineStage> GetStages(StageMask mask = null){
            List<PipelineStage> res = new List<PipelineStage>();
            for (int i = 0; i < stages.Count; i++){
                if (mask != null && mask.maskSize > i){
                    if (mask.activity[i] == true){
                        res.Add(UnityEngine.Object.Instantiate(stages[i]));
                    }
                }else{
                    res.Add(UnityEngine.Object.Instantiate(stages[i]));
                }
            }
            return res;
        }
    }

}