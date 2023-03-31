using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter.blur {

    [CreateAssetMenu(fileName = "StageThermalErosion", menuName = "Noize/Filter/Blur/ThermalErosion", order = 2)]
    public class StageThermalErosion: PipelineStage {

        static ThermalErosionFilterDelegate job = ThermalErosionFilter.Schedule;

        [Range(1, 32)]
        public int iterations = 1;
        [Range(1, 90)]
        public int talus = 45;
        public float increment = 0.5f;
        public float meshHeightWidthRatio = 0.75f;


        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
            jobHandle = job(d.data, (float)talus, increment, meshHeightWidthRatio, iterations, d.resolution, dependency);
        }
    }
}