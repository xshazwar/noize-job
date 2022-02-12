using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.processing.cpu.mutate;

[RequireComponent(typeof(FBMSource))]
public class FlowMapFilter : MonoBehaviour
{
    static FillArrayJobDelegate fillStage = FillArrayJob.ScheduleParallel;
    static FlowMapStepComputeFlowDelegate flowStage =  FlowMapStepComputeFlow<ComputeFlowStep, ReadTileData, RWTileData>.ScheduleParallel;
    static FlowMapStepUpdateWaterDelegate waterStage = FlowMapStepUpdateWater<UpdateWaterStep, ReadTileData, RWTileData>.ScheduleParallel;
    static FlowMapWriteValuesDelegate writeStage = FlowMapWriteValues<CreateVelocityField, ReadTileData, WriteTileData>.ScheduleParallel;
    static MapNormalizeValuesDelegate normStage = MapNormalizeValues<NormalizeMap, RWTileData>.ScheduleParallel;

    public DataSourceSingleChannel<FBMSource> dataSource;
    
    public const int READ = 0;
    public const int WRITE = 1;

    bool arraysReady;
    NativeArray<float>[] waterMap;
    NativeArray<float>[] flowMapN;
    NativeArray<float>[] flowMapS;
    NativeArray<float>[] flowMapE;
    NativeArray<float>[] flowMapW;

    JobHandle jobHandle;
    
    [Range(1, 32)]
    public int iterations;
    public bool enabled;
    bool triggered;
    bool enqueueFinished;

    void InitArrays(int size){
        if(arraysReady){
            return;
        }
        UnityEngine.Profiling.Profiler.BeginSample("Allocate Arrays");
        waterMap[READ] = new NativeArray<float>(size, Allocator.Persistent);
        waterMap[WRITE] = new NativeArray<float>(size, Allocator.Persistent);
        flowMapN[READ] = new NativeArray<float>(size, Allocator.Persistent);
        flowMapN[WRITE] = new NativeArray<float>(size, Allocator.Persistent);
        flowMapS[READ] = new NativeArray<float>(size, Allocator.Persistent);
        flowMapS[WRITE] = new NativeArray<float>(size, Allocator.Persistent);
        flowMapE[READ] = new NativeArray<float>(size, Allocator.Persistent);
        flowMapE[WRITE] = new NativeArray<float>(size, Allocator.Persistent);
        flowMapW[READ] = new NativeArray<float>(size, Allocator.Persistent);
        flowMapW[WRITE] = new NativeArray<float>(size, Allocator.Persistent);
        arraysReady = true;
        UnityEngine.Profiling.Profiler.EndSample();
        Debug.Log("Arrays Ready");
    }

    void Start(){
        dataSource = new DataSourceSingleChannel<FBMSource>();
        dataSource.source = GetComponent<FBMSource>();
        triggered = false;
        enqueueFinished = true;
        arraysReady = false;
        iterations = 5;
        waterMap = new NativeArray<float>[2];
        flowMapN = new NativeArray<float>[2];
        flowMapS = new NativeArray<float>[2];
        flowMapE = new NativeArray<float>[2];
        flowMapW = new NativeArray<float>[2];
    }

    public IEnumerator FilterSteps(){
        NativeSlice<float> src;
        int res; int tileSize;
        UnityEngine.Profiling.Profiler.BeginSample("Get Upstream Handle");
        dataSource.GetData(out src, out res, out tileSize);
        Debug.Log(res);
        UnityEngine.Profiling.Profiler.EndSample();
        InitArrays(res * res);

        JobHandle[] handles = new JobHandle[iterations * 2];
        for (int i = 0; i < 2 * iterations; i += 2){
            UnityEngine.Profiling.Profiler.BeginSample("Enqueue Step");
            if (i == 0){
                JobHandle fillHandle = fillStage(waterMap[READ], res, 0.0001f, default);
                handles[0] = flowStage(
                        src,
                        new NativeSlice<float>(waterMap[READ]),
                        new NativeSlice<float>(flowMapN[READ]),
                        new NativeSlice<float>(flowMapN[WRITE]),
                        new NativeSlice<float>(flowMapS[READ]),
                        new NativeSlice<float>(flowMapS[WRITE]),
                        new NativeSlice<float>(flowMapE[READ]),
                        new NativeSlice<float>(flowMapE[WRITE]),
                        new NativeSlice<float>(flowMapW[READ]),
                        new NativeSlice<float>(flowMapW[WRITE]),
                        res,
                        fillHandle);
                handles[1]  = waterStage(
                        new NativeSlice<float>(waterMap[READ]),
                        new NativeSlice<float>(waterMap[WRITE]),
                        new NativeSlice<float>(flowMapN[READ]),
                        new NativeSlice<float>(flowMapS[READ]),
                        new NativeSlice<float>(flowMapE[READ]),
                        new NativeSlice<float>(flowMapW[READ]),
                        res,
                        handles[i]);
            }else{
                handles[i] = flowStage(
                        src,  
                        new NativeSlice<float>(waterMap[READ]),
                        new NativeSlice<float>(flowMapN[READ]),
                        new NativeSlice<float>(flowMapN[WRITE]),
                        new NativeSlice<float>(flowMapS[READ]),
                        new NativeSlice<float>(flowMapS[WRITE]),
                        new NativeSlice<float>(flowMapE[READ]),
                        new NativeSlice<float>(flowMapE[WRITE]),
                        new NativeSlice<float>(flowMapW[READ]),
                        new NativeSlice<float>(flowMapW[WRITE]),
                        res,
                        handles[i - 1]);
                handles[i + 1]  = waterStage(
                        new NativeSlice<float>(waterMap[READ]),
                        new NativeSlice<float>(waterMap[WRITE]),
                        new NativeSlice<float>(flowMapN[READ]),
                        new NativeSlice<float>(flowMapS[READ]),
                        new NativeSlice<float>(flowMapE[READ]),
                        new NativeSlice<float>(flowMapW[READ]),
                        res,
                        handles[i]);
            }
            UnityEngine.Profiling.Profiler.EndSample();
            yield return null;   
        }

        JobHandle writeHandle = writeStage(
                        src,
                        new NativeSlice<float>(flowMapN[READ]),
                        new NativeSlice<float>(flowMapS[READ]),
                        new NativeSlice<float>(flowMapE[READ]),
                        new NativeSlice<float>(flowMapW[READ]),
                        res,
                        handles[(iterations * 2) - 1]);
        jobHandle = normStage(
                        src,
                        res,
                        writeHandle
        );
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
            dataSource?.UpdateImageChannel();
            triggered = false;
            DisposeArrays();
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

    public void DisposeArrays(){
        waterMap[READ].Dispose();
        waterMap[WRITE].Dispose();
        flowMapN[READ].Dispose();
        flowMapN[WRITE].Dispose();
        flowMapS[READ].Dispose();
        flowMapS[WRITE].Dispose();
        flowMapE[READ].Dispose();
        flowMapE[WRITE].Dispose();
        flowMapW[READ].Dispose();
        flowMapW[WRITE].Dispose();
        arraysReady = false;
    }

    public void OnDestroy(){
        if(!arraysReady){
            return;
        }
        DisposeArrays();

    }
}
