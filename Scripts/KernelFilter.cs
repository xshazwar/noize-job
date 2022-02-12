using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.processing.cpu.mutate;

public class DataSourceSingleChannel<T> : IProvideTiles, IUpdateImageChannel where T: MonoBehaviour, IProvideTiles, IUpdateImageChannel {
    public T source;
    
    public void GetData(out NativeSlice<float> d, out int res, out int ts){
        source.GetData(out d, out res, out ts);
    }

    public void UpdateImageChannel(){
        source.UpdateImageChannel();
    }
}

public class DataSourceMultiChannel<T> : IProvideTiles, IUpdateAllChannels where T: MonoBehaviour, IProvideTiles, IUpdateAllChannels {
    public T source;
    
    public void GetData(out NativeSlice<float> d, out int res, out int ts){
        source.GetData(out d, out res, out ts);
    }

    public void UpdateImageAllChannels(){
        source.UpdateImageAllChannels();
    }
}

[RequireComponent(typeof(FBMSource))]
public class KernelFilter : MonoBehaviour
{
    static SeperableKernelFilterDelegate job = SeparableKernelFilter.Schedule;

    public KernelFilterType filter;
    public DataSourceMultiChannel<FBMSource> dataSource;

    JobHandle jobHandle;
    
    [Range(1, 32)]
    public int iterations;
    public bool enabled;
    bool triggered;
    bool enqueueFinished;


    // Start is called before the first frame update
    void Start(){
        dataSource = new DataSourceMultiChannel<FBMSource>();
        dataSource.source = GetComponent<FBMSource>();
        triggered = false;
        enqueueFinished = true;

        iterations = 5;
    }

    public IEnumerator FilterSteps(){
        NativeSlice<float> src;
        int res; int tileSize;
        UnityEngine.Profiling.Profiler.BeginSample("Get Upstream Handle");
        dataSource.GetData(out src, out res, out tileSize);
        UnityEngine.Profiling.Profiler.EndSample();
        JobHandle[] handles = new JobHandle[iterations];
        for (int i = 0; i < iterations; i++){
            UnityEngine.Profiling.Profiler.BeginSample("Enqueue Step");
            if (i == 0){
                handles[i] = job(src, filter, res, default);
            }else{
                handles[i] = job(src, filter, res, handles[i - 1]);
            }
            UnityEngine.Profiling.Profiler.EndSample();
            yield return null;   
        }
		jobHandle = handles[iterations - 1];
        enqueueFinished = true;
    }
    void Update()
    {
		if (triggered){
            if (!enqueueFinished){
                return;
            }
            if (!jobHandle.IsCompleted){
                return;
            }
            UnityEngine.Profiling.Profiler.BeginSample("Apply Filter");
            jobHandle.Complete();
            dataSource?.UpdateImageAllChannels();
            triggered = false;
            UnityEngine.Profiling.Profiler.EndSample();
        }
        
        if (enabled && !triggered){
            UnityEngine.Profiling.Profiler.BeginSample("Start Filter Job");
            triggered = true;
            enqueueFinished = false;
            StartCoroutine(FilterSteps());
            // FilterTexture();
            UnityEngine.Profiling.Profiler.EndSample();
            enabled = false;
        }
    }
}
