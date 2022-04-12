using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {

    [CreateAssetMenu(fileName = "CropStage", menuName = "Noize/Filter/CenterCropResolution", order = 2)]
    public class CropStage: PipelineStage {
		static CropJobDelegate job = CropJob<ReadTileData, WriteTileData>.ScheduleParallel;
        public override void Schedule( StageIO req, JobHandle dep){  
            DownsampleData d = (DownsampleData) req;
            jobHandle = job(d.inputData, d.inputResolution, d.data, d.resolution, dep);
        }
    }
}