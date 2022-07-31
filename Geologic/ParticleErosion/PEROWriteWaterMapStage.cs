using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;


using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.geologic {

    [CreateAssetMenu(fileName = "PEROWriteWaterMapStage", menuName = "Noize/Geologic/PEROWriteWaterMap", order = 2)]
    public class PEROWriteWaterMapStage: PipelineStage {
        
        static LockJobDelegate lockJob = LockJob.Schedule;

        private string getBufferName(GeneratorData d, string alias){
            return $"{d.xpos}_{d.zpos}__{d.resolution}__{alias}";
        }

        public override bool IsSchedulable(PipelineWorkItem job){
            if(job.stageManager == null){
                return false;
            }
            bool[] exists = new bool[]{
                job.stageManager.BufferExists<NativeParallelHashMap<int, int>>(getBufferName((GeneratorData)job.data,"PARTERO_CATCHMENT")),
                job.stageManager.BufferExists<NativeParallelHashMap<PoolKey, Pool>>(getBufferName((GeneratorData)job.data,"PARTERO_POOLS")),                
            };
            if(exists.Contains<bool>(false)){
                return false;
            }
            bool[] notReady = new bool[] {
                job.stageManager.IsLocked<NativeParallelHashMap<int, int>>(getBufferName((GeneratorData)job.data,"PARTERO_CATCHMENT")),
                job.stageManager.IsLocked<NativeParallelHashMap<PoolKey, Pool>>(getBufferName((GeneratorData)job.data,"PARTERO_POOLS")),
                job.stageManager.IsLocked<NativeArray<float>>(getBufferName((GeneratorData)job.data,"PARTERO_WATERMAP_POOL"))
            };
            if(notReady.Contains<bool>(true)){
                return false;
            }
            return true;
        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;

            // Writen by
            NativeParallelHashMap<PoolKey, Pool> pools = requirements.stageManager.GetBuffer<PoolKey, Pool, NativeParallelHashMap<PoolKey, Pool>>(getBufferName((GeneratorData)requirements.data,"PARTERO_POOLS"), 512);
            NativeParallelHashMap<int, int> catchmentMap = requirements.stageManager.GetBuffer<int, int, NativeParallelHashMap<int, int>>(getBufferName((GeneratorData)requirements.data,"PARTERO_CATCHMENT"), dataLength);
            NativeArray<float> waterMap = requirements.stageManager.GetBuffer<float, NativeArray<float>>(getBufferName((GeneratorData)requirements.data,"PARTERO_WATERMAP_POOL"), dataLength);
            NativeSlice<float> waterMapSlice = new NativeSlice<float>(waterMap);

            JobHandle poolJob = default(JobHandle);
            JobHandle lockHandle = lockJob(poolJob);
            
            string bufferName = getBufferName((GeneratorData)requirements.data,"PARTERO_WATERMAP_POOL");
            requirements.stageManager.TrySetLock<NativeArray<float>>(bufferName, lockHandle, poolJob);
        }
    }
}