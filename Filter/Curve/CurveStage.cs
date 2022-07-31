using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "CurveStage", menuName = "Noize/Filter/CurveFilter", order = 2)]
    public class CurveStage: PipelineStage {
        static CurveJobScheduleDelegate job = CurveJob<CurveOperator, RWTileData>.ScheduleParallel;
        public AnimationCurve unityCurve;
        private NativeArray<float> curve;
        public int samples = 256;
        private NativeArray<float> tmp;

        void Awake(){
            if (unityCurve != null){
                ExtractCurve();
            }
        }   

        private void ExtractCurve(){
            if (!curve.IsCreated){
                curve = new NativeArray<float>(samples, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            
            for (int i = 0; i < samples; i++){
                curve[i] = unityCurve.Evaluate( (float) i / samples );
            }
        }

        public override void ResizeNativeContainers(int size){
            // Resize containers
            
            if(tmp.IsCreated){
                tmp.Dispose();
            }
            tmp = new NativeArray<float>(dataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
            jobHandle = job(
                d.data,
                tmp,
                new NativeSlice<float>(curve),
                d.resolution,
                dependency
            );
        }

        public override void OnDestroy()
        {
            if (curve.IsCreated){
                curve.Dispose();
                curve = default;
            }
            if(tmp.IsCreated){
                tmp.Dispose();
                tmp = default;
            }
        }
    }
}