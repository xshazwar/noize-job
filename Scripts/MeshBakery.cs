using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Unity.Jobs;
using Unity.Collections;

using UnityEngine;

using xshazwar.noize.mesh;
// using xshazwar.noize.pipeline;

namespace xshazwar.noize.scripts {

    public class MeshBakeOrder {
        public string uuid;
        public int meshID;
        public Action<string> onCompleteBake;
    }

    [AddComponentMenu("Noize/MeshBakery", 0)]
    public class MeshBakery : MonoBehaviour {
        
        private ConcurrentQueue<MeshBakeOrder> workQueue;
        private List<MeshBakeOrder> inProgress;
        static BakeManyJobDelegate job = BakeManyJob.ScheduleParallel;
        private NativeArray<int> meshes;
        private bool isRunning;

        private JobHandle jobHandle;

        public void Awake(){
            isRunning = false;
            meshes = new NativeArray<int>(new int[] {}, Allocator.Persistent);
            workQueue = new ConcurrentQueue<MeshBakeOrder>();
            inProgress = new List<MeshBakeOrder>();
        }

        public void Destroy(){
            meshes.Dispose();
        }

        public void Update(){
            if (!isRunning){
                ServiceQueue();
                return;
            }
            if (!jobHandle.IsCompleted){
                return;
            }
            jobHandle.Complete();
            isRunning = false;
            JobFinished();
        }

        public void Enqueue(MeshBakeOrder order){
            workQueue.Enqueue(order);
        }

        public void ServiceQueue(){
            if (workQueue.Count == 0){
                return;
            }
            isRunning = true;
            MeshBakeOrder o;
            List<int> meshIDs = new List<int>();
            inProgress = new List<MeshBakeOrder>();
            while(workQueue.TryDequeue(out o)){
                meshIDs.Add(o.meshID);
                inProgress.Add(o);
            }
            meshes.Dispose();
            meshes = new NativeArray<int>(meshIDs.ToArray(), Allocator.Persistent);
            jobHandle = job(meshes, default);
        }

        public void JobFinished(){
            Debug.Log($"Mesh batch of size {inProgress.Count} complete");
            foreach(MeshBakeOrder o in inProgress){
                o.onCompleteBake.Invoke(o.uuid);
            }
        }

    }
}