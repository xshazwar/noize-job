using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.mesh {

    [CreateAssetMenu(fileName = "MeshBakeStage", menuName = "Noize/Mesh/BakeMeshStage", order = 2)]
    public class MeshBakeStage: PipelineStage {
		static BakeJobDelegate job = BakeJob.Schedule;
        public override void Schedule( StageIO req ){
            MeshStageData d = (MeshStageData) req;

			jobHandle = job(d.mesh.GetInstanceID(), default);
        }
    }
}