using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.mesh {

    [CreateAssetMenu(fileName = "MeshBakeStage", menuName = "Noize/Mesh/BakeMeshStage", order = 2)]
    public class MeshBakeStage: PipelineStage {
		static BakeSingleJobDelegate job = BakeSingleJob.Schedule;
        // public override void Schedule( StageIO req, JobHandle dep ){
        //     MeshStageData d = (MeshStageData) req;

		// 	jobHandle = job(d.mesh.GetInstanceID(), dep);
        // }


        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            MeshStageData d = (MeshStageData) requirements.data;
            jobHandle = job(d.mesh.GetInstanceID(), dependency);
        }
    }
}