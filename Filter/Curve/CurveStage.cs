using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.cpu.mutate {

    [CreateAssetMenu(fileName = "CurveStage", menuName = "Noize/Filter/CurveFilter", order = 2)]
    public class CurveStage: PipelineStage {
        static CurveJobScheduleDelegate job = CurveJob<CurveOperator, RWTileData>.ScheduleParallel;
        public AnimationCurve unityCurve;
        private NativeArray<float> curve;
        public int samples = 256;

        void OnValidate(){
            if (unityCurve != null){
                ExtractCurve();
            }
        }

        void Awake(){
            if (unityCurve != null){
                ExtractCurve();
            }
        }

        private void ExtractCurve(){
            try{
                curve.Dispose();
            }catch(Exception err){}
            curve = new NativeArray<float>(samples, Allocator.Persistent);
            for (int i = 0; i < samples; i++){
                curve[i] = unityCurve.Evaluate( (float) i / samples );
            }
        }

        public override void Schedule( StageIO req ){
            GeneratorData d = (GeneratorData) req;
            NativeArray<float> tmp = new NativeArray<float>(d.data.Length, Allocator.Persistent);
            jobHandle = tmp.Dispose(job(
                d.data,
                tmp,
                new NativeSlice<float>(curve),
                d.resolution,
                default
            ));
        }

        public override void OnDestroy()
        {
            if (curve.IsCreated){
                curve.Dispose();
            }
        }
    }
}