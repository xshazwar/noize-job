using Unity.Jobs;
using UnityEngine;

namespace xshazwar.noize.mesh {
    public struct BakeSingleJob : IJob
    {
        public int meshId;

        public void Execute()
        {
            Physics.BakeMesh(meshId, false);
        }

        public static JobHandle Schedule(int meshId, JobHandle deps){
            var job = new BakeSingleJob();     
            job.meshId = meshId;
            return job.Schedule(deps);
        }
    }

    public delegate JobHandle BakeSingleJobDelegate(int meshId, JobHandle deps);

}