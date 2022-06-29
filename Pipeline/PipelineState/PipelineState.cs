using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

namespace xshazwar.noize.pipeline {

    public class PipelineState<T> where T : struct { 

        int warmPoolMin;
        int warmPoolMax;
        int bufferSize;

        // bufferName -> {guid-of-work}.{buffer-alias}
        private Dictionary<string, NativeArray<T>> buffers;
        private Queue<NativeArray<T>> pool;

        public PipelineState(int bufferSize, int warmPoolMin = 2, int warmPoolMax = 4){
            this.bufferSize = bufferSize;
            this.warmPoolMin = warmPoolMin;
            this.warmPoolMax = warmPoolMax;
            pool = new Queue<NativeArray<T>>();
        }

        public NativeArray<T> GetBuffer(string key){
            if (buffers == null){
                buffers = new Dictionary<string, NativeArray<T>>();
            }
            if (!buffers.ContainsKey(key) && pool.Count <= 0){
                buffers[key] = new NativeArray<T>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }else if (pool.Count > 0){
                buffers[key] = pool.Dequeue();
            }
            return buffers[key];
        }

        public bool BufferExists(string key){
            return buffers.ContainsKey(key);
        }

        public void ReleaseBuffer(string key){
            NativeArray<T> released = buffers[key];
            pool.Enqueue(released);
            buffers.Remove(key);
        }

        public void ServiceWarmPool(){
            if (pool.Count < warmPoolMin){
                pool.Enqueue(new NativeArray<T>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory));
            }else if (pool.Count > warmPoolMax){
                NativeArray<T> excess = pool.Dequeue();
                excess.Dispose();
            }
        }

        public void Destroy(){
            while (pool.Count > 0){
                pool.Dequeue().Dispose();
            }
            foreach(var kvp in buffers){
                kvp.Value.Dispose();
            }
        }
    }
}