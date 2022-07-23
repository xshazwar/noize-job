using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

namespace xshazwar.noize.pipeline {

    public enum PipelineBufferOperation {
        READ,
        WRITE
    }

    public static class ConstraintsLinear<T> where T : unmanaged, IEquatable<T> {
        public static readonly HashSet<Type> SUPPORTED_TYPES = new HashSet<Type> {
            typeof(NativeArray<T>),
            typeof(NativeList<T>),
            typeof(NativeParallelHashSet<T>)
        };

        public static readonly Dictionary<Type, Type> conversion = new Dictionary<Type, Type> {
                {typeof(NativeArray<T>), typeof(NativeArrayState<T>)},
                {typeof(NativeList<T>), typeof(NativeListState<T>)},
                {typeof(NativeParallelHashSet<T>), typeof(NativeParallelHashSetState<T>)}
        };

        public static IManageBuffer<C>? GetBufferManager<C>(){
            if (!SUPPORTED_TYPES.Contains(typeof(C))){
                return null;
            }
            foreach(Type t in conversion.Keys){
                if(t == typeof(C)){
                    return (IManageBuffer<C>) Activator.CreateInstance(conversion[t]);
                }
            }
            return null;
        }
    }


    public static class ConstraintsKeyValue<TKey, TValue> where TKey : struct, IEquatable<TKey> where TValue: struct{
        public static readonly HashSet<Type> SUPPORTED_TYPES = new HashSet<Type> {
            typeof(NativeParallelHashMap<TKey, TValue>),
            typeof(NativeParallelMultiHashMap<TKey, TValue>)
        };

        public static readonly Dictionary<Type, Type> conversion = new Dictionary<Type, Type> {
                {typeof(NativeParallelHashMap<TKey, TValue>), typeof(NativeParallelHashMapState<TKey, TValue>)},
                {typeof(NativeParallelMultiHashMap<TKey, TValue>), typeof(NativeParallelMultiHashMapState<TKey, TValue>)}

        };

        public static IManageBuffer<C>? GetBufferManager<C>(){
            if (!SUPPORTED_TYPES.Contains(typeof(C))){
                return null;
            }
            foreach(Type t in conversion.Keys){
                if(t == typeof(C)){
                    return (IManageBuffer<C>) Activator.CreateInstance(conversion[t]);
                }
            }
            return null;
        }
    }

    public interface IBaseBufferManager {
        public bool BufferExists(string key);
        public bool ReleaseBuffer(string key);
        public void Destroy();
        public void RegisterCallback(string key, Action action);
        public void RemoveCallback(string key, Action action);
        public void TriggerUpdateCallbacks(string key);
    }
    public interface IManageBuffer<out T> : IBaseBufferManager {
        public T GetBuffer(string key, int size);
    }

    
    public class NativeParallelHashMapState<K, V>: DisposablePipelineState<NativeParallelHashMap<K, V>> where K : struct, IEquatable<K> where V: struct {
        public NativeParallelHashMapState(){}

        public override NativeParallelHashMap<K,V> CreateInstance(int size){
            Debug.Log($"allocate new NativeParallelHashMap<{typeof(K)},{typeof(V)}>: {size}");
            return new NativeParallelHashMap<K,V>(size, Allocator.Persistent);
        }
    }
    
    public class NativeParallelMultiHashMapState<K, V>: DisposablePipelineState<NativeParallelMultiHashMap<K, V>> where K : struct, IEquatable<K> where V: struct {
        public NativeParallelMultiHashMapState(){}

        public override NativeParallelMultiHashMap<K,V> CreateInstance(int size){
            Debug.Log($"allocate new NativeParallelMultiHashMap<{typeof(K)},{typeof(V)}>: {size}>");
            return new NativeParallelMultiHashMap<K,V>(size, Allocator.Persistent);
        }
    }


    public class NativeParallelHashSetState<T> : DisposablePipelineState<NativeParallelHashSet<T>> where T: unmanaged, IEquatable<T> {
        
        public NativeParallelHashSetState(){}
        
        public override NativeParallelHashSet<T> CreateInstance(int size){
            Debug.Log($"allocate new NativeParallelHashSet<{typeof(T)}>: {size}");
            return new NativeParallelHashSet<T>(size, Allocator.Persistent);
        }
    }

    public class NativeListState<T> : DisposablePipelineState<NativeList<T>> where T: unmanaged {
        
        public NativeListState(){}
        
        public override NativeList<T> CreateInstance(int size){
            Debug.Log($"allocate new NativeList<{typeof(T)}>: {size}");
            return new NativeList<T>(size, Allocator.Persistent);
        }
    }

    public class NativeArrayState<T> : DisposablePipelineState<NativeArray<T>> where T: unmanaged {
        
        public NativeArrayState() : base(){}
        
        public override NativeArray<T> CreateInstance(int size){
            Debug.Log($"allocate new NativeArray<{typeof(T)}>: {size}");
            return new NativeArray<T>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        public override void Dispose(NativeArray<T> item){
            item.Dispose();
        }
    }

    public abstract class DisposablePipelineState<C> : BasePipelineState<C>, IManageBuffer<C> where C : IDisposable {
        public DisposablePipelineState(){}

        public override void Dispose(C item){
            item.Dispose();
        }

    }

    public abstract class BasePipelineState<C>: IManageBuffer<C> { 

        // bufferName -> {guid-of-work}.{buffer-alias}
        private Dictionary<string, Action> notifier;
        
        private Dictionary<string, C> buffers;
        private Queue<C> pool;

        public BasePipelineState(){}

        public C GetBuffer(string key, int size){
            if (buffers == null){
                buffers = new Dictionary<string, C>();
            }
            if (!buffers.ContainsKey(key)){
                buffers[key] = CreateInstance(size);
            }
            return buffers[key];
        }

        public bool BufferExists(string key){
            return buffers.ContainsKey(key);
        }

        public bool ReleaseBuffer(string key){
            Dispose(buffers[key]);
            return buffers.Remove(key);
        }

        public void RegisterCallback(string key, Action action){
            if (notifier == null){
                notifier = new Dictionary<string, Action>();
            }
            if(!notifier.ContainsKey(key)){
                notifier[key] = () => {};
            }
            notifier[key] += action;
            
        }

        public void RemoveCallback(string key, Action action){
            if(!buffers.ContainsKey(key)){
                throw new ArgumentException($"missing buffer {key}");
            }
            if(notifier.ContainsKey(key)){
                notifier[key] -= action;
            }
        }

        public void TriggerUpdateCallbacks(string key){
            if(notifier.ContainsKey(key)){
                notifier[key]?.Invoke();
            }
        }

        public void Destroy(){
            foreach(var key in buffers.Keys.ToArray()){
                Debug.Log($"Disposing of remaining buffer {key}");
                ReleaseBuffer(key);
            }
        }

        public abstract void Dispose(C item);

        public abstract C CreateInstance(int size);
    }
}