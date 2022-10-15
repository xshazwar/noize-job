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
        private HashSet<string> activeOrders;
        static BakeManyJobDelegate job = BakeManyJob.ScheduleParallel;
        public int maxBatch = 2;
        private NativeArray<int> meshes;
        private bool isRunning;
        private JobHandle jobHandle;
    #if UNITY_EDITOR
        protected System.Diagnostics.Stopwatch wall;
    #endif

        public void Awake(){
            isRunning = false;
            workQueue = new ConcurrentQueue<MeshBakeOrder>();
            inProgress = new List<MeshBakeOrder>();
            activeOrders = new HashSet<string>();
        }

        public void Destroy(){
            if(meshes.IsCreated){
                meshes.Dispose();
            }
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
            if(activeOrders.Contains(order.uuid)){
                // duplicated baking requests crash unity so we ignore them
                return;
            }
            activeOrders.Add(order.uuid);
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
            int batchCount = maxBatch;
            while(workQueue.TryDequeue(out o)){
                meshIDs.Add(o.meshID);
                inProgress.Add(o);
                batchCount -= 1;
                if (batchCount <= 0){
                    break;
                }
            }
            meshes = new NativeArray<int>(meshIDs.ToArray(), Allocator.Persistent);
            #if UNITY_EDITOR
                wall = System.Diagnostics.Stopwatch.StartNew();
                Debug.LogWarning($"Mesh batch of size {inProgress.Count} starting");
            #endif
            jobHandle = job(meshes, default);
        }

        public void JobFinished(){
            foreach(MeshBakeOrder o in inProgress){
                o.onCompleteBake.Invoke(o.uuid);
                activeOrders.Remove(o.uuid);
            }
            meshes.Dispose();
            meshes = default;
            #if UNITY_EDITOR
                wall.Stop();
                Debug.LogWarning($"Mesh batch of size {inProgress.Count} complete in {wall.ElapsedMilliseconds}ms");
            #endif
        }

    }
}