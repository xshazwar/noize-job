using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using Unity.Collections;
using Unity.Jobs;

namespace xshazwar.noize.pipeline {
    public class HandleLock {
        public JobHandle jobHandle;
        private JobHandle spyHandle;

        public HandleLock(JobHandle handle, JobHandle spy){
            jobHandle = handle;
            spyHandle = spy;
        }

        public bool isLocked(){
            if(!jobHandle.IsCompleted){
                return true;
            }
            return !JobHandle.CheckFenceIsDependencyOrDidSyncFence(spyHandle, jobHandle);
        }
    }

    public struct LockJob : IJob {
        // Non-op job so we can compare it's handle to the actual work
        // to see if the scheduled work is complete using
        // JobHandle.CheckFenceIsDependencyOrDidSyncFence

        public void Execute(){}
        public static JobHandle Schedule(JobHandle dependency){
            var job = new LockJob();
            return job.Schedule(dependency);
        }
    }
    public delegate JobHandle LockJobDelegate(JobHandle handle);

}