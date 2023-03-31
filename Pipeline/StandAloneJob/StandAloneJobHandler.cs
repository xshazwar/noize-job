using UnityEngine;
using Unity.Jobs;

namespace xshazwar.noize.pipeline {

    public class StandAloneJobHandler {
        
        public bool isRunning = false;
        public JobHandle handle = default(JobHandle);
        
        public StandAloneJobHandler(){
        }

        public bool TrackJob(JobHandle handle){
            this.handle = handle;
            isRunning = true;
            return true;
        }

        public bool JobComplete(){
            if(isRunning && handle.IsCompleted){
                return true;
            }
            return false;
        }

        public bool CloseJob(){
            if(!JobComplete()){
                return false;
            }
            handle.Complete();
            isRunning = false;
            return true;
        }
    }
}