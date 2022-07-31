using Unity.Jobs;
using Unity.Collections;
using UnityEngine;

namespace xshazwar.noize.mesh {
    public struct BakeManyJob : IJobFor
    {
        public NativeArray<int> meshIds;

        public void Execute(int i)
        {
            int meshId = meshIds[i];
            Physics.BakeMesh(meshId, false);
        }

        public static JobHandle ScheduleParallel(NativeArray<int> meshIds, JobHandle deps){
            var job = new BakeManyJob();     
            job.meshIds = meshIds;
            return job.ScheduleParallel(meshIds.Length, 1, deps);
        }
    }

    public delegate JobHandle BakeManyJobDelegate(NativeArray<int> meshIds, JobHandle deps);

}