using Unity.Jobs;
using UnityEngine;

namespace xshazwar.noize.mesh {
    public struct BakeJob : IJob
    {
        public int meshId;

        public void Execute()
        {
            Physics.BakeMesh(meshId, false);
        }

        public static JobHandle Schedule(int meshId, JobHandle deps){
            var job = new BakeJob();     
            job.meshId = meshId;
            return job.Schedule(deps);
        }
    }

    public delegate JobHandle BakeJobDelegate(int meshId, JobHandle deps);

}