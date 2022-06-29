using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections;
using Unity.Jobs;

namespace xshazwar.noize.pipeline {

    public class PipelineStateManager : MonoBehaviour { 

        // key -> capacity of buffer of Type T (currently fixed as float)
        private Dictionary<int, PipelineState<float>> states;

        public NativeSlice<float>? GetReadBuffer(int size, string key){
            if (!BufferExists(size, key)){
                return null;
            }
            return new NativeSlice<float>(GetBuffer(size, key));
        }

        public NativeSlice<float> GetWriteBuffer(int size, string key){
            return new NativeSlice<float>(GetBuffer(size, key));
        }
        
        private NativeArray<float> GetBuffer(int size, string key) {
            if (states == null){
                states = new Dictionary<int, PipelineState<float>>();
            }
            if (!states.ContainsKey(size)){
                states[size] = new PipelineState<float>(size);
            }
            return states[size].GetBuffer(key);
        }

        private bool BufferExists(int size, string key){
            if (!states.ContainsKey(size)){
                return false;
            }
            return states[size].BufferExists(key);
        }

        public void ReleaseBuffer(int size, string key){
            try{
                states[size].ReleaseBuffer(key);
            } catch (NullReferenceException ner){
                Debug.LogError($"Could not release {key}: {ner}, {size} missing?");
            }
        }
        void OnDestroy(){
            foreach(var kvp in states){
                kvp.Value.Destroy();
            }
        }
    }
}