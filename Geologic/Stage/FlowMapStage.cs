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

        [Range(1, 32)]
        public int iterations = 5;
        public int resolution = 512;

        public float normMin = -.1f;
        public float normMax = .1f;
        
        static FillArrayJobDelegate fillStage = FillArrayJob.ScheduleParallel;
        static FlowMapStepComputeFlowDelegate flowStage =  FlowMapStepComputeFlow<ComputeFlowStep, ReadTileData, RWTileData>.ScheduleParallel;
        static FlowMapStepUpdateWaterDelegate waterStage = FlowMapStepUpdateWater<UpdateWaterStep, ReadTileData, RWTileData>.ScheduleParallel;
        static FlowMapWriteValuesDelegate writeStage = FlowMapWriteValues<CreateVelocityField, ReadTileData, WriteTileData>.ScheduleParallel;
        static MapNormalizeValuesDelegate normStage = MapNormalizeValues<NormalizeMap, RWTileData>.ScheduleParallel;

        private const int READ = 0;
        private const int WRITE = 1;

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
            tmp = new NativeArray<float>(size, Allocator.Persistent);
            normArgs = new NativeArray<float>(
                new float[] {normMin, normMax, normMax - normMin},
                Allocator.Persistent
            );
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

        public void DisposeArrays(){
            tmp.Dispose();
            normArgs.Dispose();
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


        void OnValidate(){}

        void Awake(){
            arraysReady = false;
            waterMap = new NativeArray<float>[2];
            flowMapN = new NativeArray<float>[2];
            flowMapS = new NativeArray<float>[2];
            flowMapE = new NativeArray<float>[2];
            flowMapW = new NativeArray<float>[2];
            InitArrays(resolution * resolution);
        }

        private void ScheduleAll(NativeSlice<float> src){
            JobHandle[] handles = new JobHandle[iterations * 2];
            for (int i = 0; i < 2 * iterations; i += 2){
                UnityEngine.Profiling.Profiler.BeginSample("Enqueue Step");
                if (i == 0){
                    JobHandle fillHandle = fillStage(waterMap[READ], resolution, 0.0001f, default);
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

        public override void Schedule( StageIO req ){
            GeneratorData d = (GeneratorData) req;
            if (d.resolution != resolution){
                throw new Exception($"Resolution mismatch in FlowMapStage. These handle fixed resolution. Stage: {resolution}, Input: {d.resolution}");
            }
            ScheduleAll(d.data);
        }
        
        public void Destroy(){
            if(!arraysReady){
                return;
            }
            DisposeArrays();

        }
    }
}