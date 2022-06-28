using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections;
using Unity.Jobs;

namespace xshazwar.noize.pipeline {

    public class PipelineStateManager : MonoBehaviour { 

        private Dictionary<int, PipelineState<float>> states;

        public NativeArray<float> GetBuffer(int size, string key) {
            if (states == null){
                states = new Dictionary<int, PipelineState<float>>();
            }
            if (!states.ContainsKey(size)){
                states[size] = new PipelineState<float>(size);
            }
            return states[size].GetBuffer(key);
        }

        public void ReleaseBuffer(int size, string key){
            states[size].ReleaseBuffer(key);
        }
        void OnDestroy(){
            foreach(var kvp in states){
                kvp.Value.Destroy();
            }
        }
    }
}