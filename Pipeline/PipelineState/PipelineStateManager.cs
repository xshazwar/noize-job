using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections;
using Unity.Jobs;

namespace xshazwar.noize.pipeline {

    public class PipelineStateManager : MonoBehaviour { 

        private Dictionary<Type, dynamic> states;

        private void InitLinearState<V, T>() where V: unmanaged, IEquatable<V> {
            states[typeof(T)] = ConstraintsLinear<V>.GetBufferManager<T>();
        }

        private void InitKVState<K, V, T>() where K: struct, IEquatable<K> where V : struct {
            states[typeof(T)] = ConstraintsKeyValue<K, V>.GetBufferManager<T>();
        }

/*
|
|      BUFFERS
|
*/

        // request a buffer by type and initial allocation like:
        // GetBuffer<float, NativeList<float>>("256_waterMap.001x001z", 256*256);

        public T GetBuffer<V, T> (
            string name,
            int size = -1,
            NativeArrayOptions options = NativeArrayOptions.UninitializedMemory
        )  where V: unmanaged, IEquatable<V> where T: struct {
            if(states == null){
                states = new Dictionary<Type, dynamic>();
            }
            if (ConstraintsLinear<V>.SUPPORTED_TYPES.Contains(typeof(T))){
                if(!states.ContainsKey(typeof(T))){
                    Debug.Log($"creating new state manager for {typeof(T)}");
                    InitLinearState<V, T>();
                }
                IManageBuffer<T> manager = states[typeof(T)];
                if(options != NativeArrayOptions.UninitializedMemory){
                    return (T) ((IManageBuffer<T>) states[typeof(T)]).GetBuffer(name, size, options);
                }
                if (size > -1){
                    return (T) ((IManageBuffer<T>) states[typeof(T)]).GetBuffer(name, size);
                }else{
                    return (T) ((IManageBuffer<T>) states[typeof(T)]).GetBuffer(name);
                }
            }
            throw new ArgumentException("type not supported");
        }

        public T GetBuffer<K, V, T> (string name, int size = -1)  where K: struct, IEquatable<K> where V : struct {
            if(states == null){
                states = new Dictionary<Type, dynamic>();
            }
            if (ConstraintsKeyValue<K, V>.SUPPORTED_TYPES.Contains(typeof(T))){
                if(!states.ContainsKey(typeof(T))){
                    Debug.Log($"creating new state manager for {typeof(T)}");
                    InitKVState<K, V, T>();
                }
                IManageBuffer<T> manager = states[typeof(T)];
                if (size > -1)
                {
                    return (T) ((IManageBuffer<T>) states[typeof(T)]).GetBuffer(name, size);
                }else{
                    return (T) ((IManageBuffer<T>) states[typeof(T)]).GetBuffer(name);
                }
                
            }
            throw new ArgumentException("type not supported");
        }

        public bool BufferExists<T>(string name){
            if(states == null || !states.ContainsKey(typeof(T))){
                return false;
            }
            return ((IManageBuffer<T>) states[typeof(T)]).BufferExists(name);
        }
        public bool ReleaseBuffer<T>(string name){
            if(states == null || !states.ContainsKey(typeof(T))){
                return false;
            }
            return ((IManageBuffer<T>) states[typeof(T)]).ReleaseBuffer(name);
        }

/*
|
|      LOCKS
|
*/

    // Optional mechanism that stages can use to indicate they're scheduling a write to the buffer

    public bool IsLocked<T>(string key){
        if(states == null || !states.ContainsKey(typeof(T))){
            return false;
        }
        return ((IManageBuffer<T>) states[typeof(T)]).IsLocked(key);
    }

    public bool TrySetLock<T>(string key, JobHandle handle, JobHandle spyHandle){
        if(states == null || !states.ContainsKey(typeof(T))){
                return false;
            }
        return ((IManageBuffer<T>) states[typeof(T)]).TrySetLock(key, ref handle, ref spyHandle);
    }

/*
|
|      CALLBACKS
|
*/

        // Since we're using a single point for all long lived native collections, we likely will be using them in multiple pipes
        // which might want to re-run when one of the dependencies changes

        public bool RegisterCallback<T>(string key, Action action){
            if(states == null || !states.ContainsKey(typeof(T))){
                return false;
            }
            ((IManageBuffer<T>) states[typeof(T)]).RegisterCallback(key, action);
            return true;
        }
        public bool RemoveCallback<T>(string key, Action action){
            if(states == null || !states.ContainsKey(typeof(T))){
                return false;
            }
            ((IManageBuffer<T>) states[typeof(T)]).RemoveCallback(key, action);
            return true;
        }
        public bool TriggerUpdateCallbacks<T>(string key){
            if(states == null || !states.ContainsKey(typeof(T))){
                return false;
            }
            ((IManageBuffer<T>) states[typeof(T)]).TriggerUpdateCallbacks(key);
            return true;
        }

        public void OnDestroy(){
            if(states == null) return;
            foreach(var kvp in states){
                ((IBaseBufferManager)kvp.Value).Destroy();
            }
        }
    }
}
