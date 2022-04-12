using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    public enum ReductionType {
        SUBTRACT,
        MULTIPLY,
        ROOTSUMSQUARES
    }
    
    [CreateAssetMenu(fileName = "ReduceStage", menuName = "Noize/Filter/ReduceFilter", order = 2)]
    public class ReduceStage: PipelineStage {
        static ReductionJobScheduleDelegate[] jobs = new ReductionJobScheduleDelegate[] {
            ReductionJob<SubtractTiles, RWTileData, ReadTileData>.ScheduleParallel,
            ReductionJob<MultiplyTiles, RWTileData, ReadTileData>.ScheduleParallel,
            ReductionJob<RootSumSquaresTiles, RWTileData, ReadTileData>.ScheduleParallel
        };
        public ReductionType operation;

        private int dataLength = 0;
        private NativeArray<float> tmp;

        public override void Schedule( StageIO req, JobHandle dep ){
            ReduceData d = (ReduceData) req;
            if(d.data.Length != dataLength){
                dataLength = d.data.Length;
                if(tmp.IsCreated){
                    tmp.Dispose();
                }
                tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            
            jobHandle = jobs[(int)operation](
                d.data,
                d.rightData,
                tmp,
                d.resolution,
                dep
            );
        }

        public override void TransformData(StageIO inputData){
            ReduceData d = (ReduceData) inputData;
            outputData = new GeneratorData {
                uuid = d.uuid,
                resolution = d.resolution,
                data  = d.data 
            };
        }

        public override void OnDestroy()
        {
            if(tmp.IsCreated){
                tmp.Dispose();
            }
        }
    }
}