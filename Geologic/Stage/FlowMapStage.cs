using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using UnityEngine.Rendering;

using xshazwar.noize.pipeline;
using xshazwar.noize.filter;

namespace xshazwar.noize.geologic {

    [CreateAssetMenu(fileName = "FlowMapStage", menuName = "Noize/Geologic/FlowMap", order = 2)]
    public class FlowMapStage: PipelineStage {

        [Range(1, 128)]
        public int iterations = 5;
        private int resolution = 0;

        public float normMin = -.1f;
        public float normMax = .1f;
        
        static FillArrayJobDelegate fillStage = FillArrayJob.ScheduleParallel;
        static FlowMapStepComputeFlowDelegate flowStage =  FlowMapStepComputeFlow<ComputeFlowStep, ReadTileData, RWTileData>.ScheduleParallel;
        static FlowMapStepUpdateWaterDelegate waterStage = FlowMapStepUpdateWater<UpdateWaterStep, ReadTileData, RWTileData>.ScheduleParallel;
        static FlowMapWriteValuesDelegate writeStage = FlowMapWriteValues<CreateVelocityField, ReadTileData, WriteTileData>.ScheduleParallel;
        static MapNormalizeValuesDelegate normStage = MapNormalizeValues<NormalizeMap, RWTileData>.ScheduleParallel;

        private const int READ = 0;
        private const int WRITE = 1;
        bool arraysInitialized = false;
        bool arraysReady;
        NativeArray<float> tmp;
        NativeArray<float> normArgs;
        NativeArray<float>[] waterMap;
        NativeArray<float>[] flowMapN;
        NativeArray<float>[] flowMapS;
        NativeArray<float>[] flowMapE;
        NativeArray<float>[] flowMapW;

        void InitArrays(int size){
            if(arraysReady){
                return;
            }
            UnityEngine.Profiling.Profiler.BeginSample("Allocate Arrays");
            normArgs = new NativeArray<float>(
                new float[] {normMin, normMax, normMax - normMin},
                Allocator.Persistent
            );
            tmp = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            waterMap[READ] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            waterMap[WRITE] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flowMapN[READ] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flowMapN[WRITE] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flowMapS[READ] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flowMapS[WRITE] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flowMapE[READ] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flowMapE[WRITE] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flowMapW[READ] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            flowMapW[WRITE] = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            arraysReady = true;
            UnityEngine.Profiling.Profiler.EndSample();
            Debug.Log("Arrays Ready");
        }

        public void DisposeArrays(){
            if(tmp.IsCreated){
                tmp.Dispose();
            }
            if(normArgs.IsCreated){
                normArgs.Dispose();
            }
            if(waterMap == null){
                return;
            }
            if(waterMap[READ].IsCreated){
                waterMap[READ].Dispose();
            }
            if(waterMap[WRITE].IsCreated){
                waterMap[WRITE].Dispose();
            }
            if(flowMapN[READ].IsCreated){
                flowMapN[READ].Dispose();
            }
            if(flowMapN[WRITE].IsCreated){
                flowMapN[WRITE].Dispose();
            }
            if(flowMapS[READ].IsCreated){
                flowMapS[READ].Dispose();
            }
            if(flowMapS[WRITE].IsCreated){
                flowMapS[WRITE].Dispose();
            }
            if(flowMapE[READ].IsCreated){
                flowMapE[READ].Dispose();
            }
            if(flowMapE[WRITE].IsCreated){
                flowMapE[WRITE].Dispose();
            }
            if(flowMapW[READ].IsCreated){
                flowMapW[READ].Dispose();
            }
            if(flowMapW[WRITE].IsCreated){
                flowMapW[WRITE].Dispose();
            }
            arraysReady = false;
        }


        void OnValidate(){}

        void Awake(){
            arraysReady = false;
            waterMap = new NativeArray<float>[2];
            flowMapN = new NativeArray<float>[2];
            flowMapS = new NativeArray<float>[2];
            flowMapE = new NativeArray<float>[2];
            flowMapW = new NativeArray<float>[2];
            arraysInitialized = true;
        }

        private void ScheduleAll(NativeSlice<float> src, JobHandle dep){
            JobHandle[] handles = new JobHandle[iterations * 2];
            for (int i = 0; i < 2 * iterations; i += 2){
                UnityEngine.Profiling.Profiler.BeginSample("Enqueue Step");
                if (i == 0){
                    JobHandle fillHandle = fillStage(waterMap[READ], resolution, 0.0001f, dep);
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
                            resolution,
                            fillHandle);
                    handles[1]  = waterStage(
                            new NativeSlice<float>(waterMap[READ]),
                            new NativeSlice<float>(waterMap[WRITE]),
                            new NativeSlice<float>(flowMapN[READ]),
                            new NativeSlice<float>(flowMapS[READ]),
                            new NativeSlice<float>(flowMapE[READ]),
                            new NativeSlice<float>(flowMapW[READ]),
                            resolution,
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
                            resolution,
                            handles[i - 1]);
                    handles[i + 1]  = waterStage(
                            new NativeSlice<float>(waterMap[READ]),
                            new NativeSlice<float>(waterMap[WRITE]),
                            new NativeSlice<float>(flowMapN[READ]),
                            new NativeSlice<float>(flowMapS[READ]),
                            new NativeSlice<float>(flowMapE[READ]),
                            new NativeSlice<float>(flowMapW[READ]),
                            resolution,
                            handles[i]);
                }
                UnityEngine.Profiling.Profiler.EndSample();  
            }

            JobHandle writeHandle = writeStage(
                            src,
                            new NativeSlice<float>(flowMapN[READ]),
                            new NativeSlice<float>(flowMapS[READ]),
                            new NativeSlice<float>(flowMapE[READ]),
                            new NativeSlice<float>(flowMapW[READ]),
                            resolution,
                            handles[(iterations * 2) - 1]);
            jobHandle = normStage(
                            src,
                            tmp,
                            normArgs,
                            resolution,
                            writeHandle
            );
        }

        public override void Schedule( StageIO req, JobHandle dep ){
            GeneratorData d = (GeneratorData) req;
            if (!arraysInitialized){
                Awake();
            }
            if (d.resolution != resolution){
                Debug.Log("New resolution requires creation of all buffers, expect alloc");
                resolution = d.resolution;
                DisposeArrays();
                InitArrays(resolution * resolution);
            }
            ScheduleAll(d.data, dep);
        }
        
        public override void OnDestroy(){
            DisposeArrays();

        }
    }
}