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
        ROOTSUMSQUARES,
        MAX,
        MIN
    }
    
    [CreateAssetMenu(fileName = "ReduceStage", menuName = "Noize/Filter/ReduceFilter", order = 2)]
    public class ReduceStage: PipelineStage {
        static ReductionJobScheduleDelegate[] jobs = new ReductionJobScheduleDelegate[] {
            ReductionJob<SubtractTiles, RWTileData, ReadTileData>.ScheduleParallel,
            ReductionJob<MultiplyTiles, RWTileData, ReadTileData>.ScheduleParallel,
            ReductionJob<RootSumSquaresTiles, RWTileData, ReadTileData>.ScheduleParallel,
            ReductionJob<MaxTiles, RWTileData, ReadTileData>.ScheduleParallel,
            ReductionJob<MinTiles, RWTileData, ReadTileData>.ScheduleParallel
        };
        public ReductionType operation;
        private NativeArray<float> tmp;
        public override void ResizeNativeContainers(int size){
            // Resize containers
            
            if(tmp.IsCreated){
                tmp.Dispose();
            }
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<ReduceData>(requirements);
            ReduceData d = (ReduceData) requirements.data;
            jobHandle = jobs[(int)operation](
                d.data,
                d.rightData,
                tmp,
                d.resolution,
                dependency
            );
        }

        public override void TransformData(PipelineWorkItem inputData){
            ReduceData d = (ReduceData) inputData.data;
            inputData.data = new GeneratorData {
                uuid = d.uuid,
                resolution = d.resolution,
                data  = d.data,
                xpos = d.xpos,
                zpos = d.zpos
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