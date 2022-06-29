using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "CropStage", menuName = "Noize/Filter/CenterCropResolution", order = 2)]
    public class CropStage: PipelineStage {
		static CropJobDelegate job = CropJob<ReadTileData, WriteTileData>.ScheduleParallel;
        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency){
            DownsampleData d = (DownsampleData) requirements.data;
            jobHandle = job(d.inputData, d.inputResolution, d.data, d.resolution, dependency);
        }
    }
}