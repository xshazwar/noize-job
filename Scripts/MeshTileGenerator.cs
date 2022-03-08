using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;

using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;
using xshazwar.noize.cpu.mutate;
using xshazwar.noize.scripts;
using xshazwar.noize.mesh;

namespace xshazwar.noize.scripts {

    public class TileRequest{
        public string uuid;
        public Vector2Int pos;
    }

    public class MeshTileGenerator : MonoBehaviour {
        
        public GeneratorPipeline dataSource;
        public GeneratorPipeline meshPipeline;

        public int tileHeight = 1000;
        public int tileSize = 1000;
        public int resolution = 1000;
        public int margin = 5;
        public bool jobBakesCollider;

        private Dictionary<string, TileRequest> activeTiles;
        private Dictionary<Vector2Int, GameObject> children;
        public ConcurrentQueue<TileRequest> workQueue;

        private NativeArray<float> backingData;

        public Material meshMaterial;

        public Vector2Int target;

        public Action<StageIO> upstreamData;
        public Action<StageIO> upstreamMesh;

        public bool RunMe;
        private bool isRunning;
        void Start()
        {
            RunMe = false;
            isRunning = false;
            upstreamData += DataAvailable;
            upstreamMesh += MeshComplete;
            activeTiles = new Dictionary<string, TileRequest>();
            workQueue = new ConcurrentQueue<TileRequest>();
            children = new Dictionary<Vector2Int, GameObject>();
            backingData = new NativeArray<float>(calcTotalResolution() * calcTotalResolution(), Allocator.Persistent);
        }

        public void Update(){
            if(!isRunning){
                if (workQueue.Count > 0){
                    TileRequest req;
                    if (workQueue.TryDequeue(out req)){
                        isRunning = true;
                        activeTiles[req.uuid] = req;
                        RequestTileData(req);
                        return;
                    }
                }
            }
            if (RunMe == true){
                Debug.Log("Caught Run Signal");
                Enqueue("iamatile!", target);
                RunMe = false;
            }
        }

        public void Remove(Vector2Int pos){
            if (!children.ContainsKey(pos)){
                throw new Exception("Child exists at this position");
            }
            Destroy(children[pos]);
            children.Remove(pos);
            Debug.Log($"Removing Child at {pos}");
        }
        public void Enqueue(string id, Vector2Int pos){
            if (children.ContainsKey(pos)){
                throw new Exception("Child exists at this position");
            }
            Debug.Log($"Enqueued {id}");
            workQueue.Enqueue(new TileRequest{
                uuid = id,
                pos = pos
            });
        }
        private int calcTotalResolution(){
            double patchRes = resolution / tileSize;
            return resolution + (2 * (int) Mathf.Ceil((float) (margin * patchRes)));
        }

        private int calcMarginPix(){
            return (int) ((calcTotalResolution() - resolution) / 2);
        }

        private float calculateActualMargin(){
            return 0.5f * (((float) (calcTotalResolution() * ( 1.0 / resolution) * tileSize)) - (float) tileSize);
        }

        private void RequestTileData(TileRequest req){
            Debug.Log($"requesting data for {req.uuid}");
            dataSource.Enqueue(
                new GeneratorData {
                    uuid = req.uuid,
                    resolution = calcTotalResolution(),
                    xpos = resolution *  req.pos.x,
                    zpos = resolution *  req.pos.y,
                    data = new NativeSlice<float>(backingData)
                },
                upstreamData
            );
        }

        private void CreateChildMesh(Vector2Int pos, ref MeshStageData data){
            data.mesh = new Mesh();
            GameObject go = new GameObject(pos.ToString());
            go.transform.parent = this.gameObject.transform;
            go.transform.position = new Vector3(
                (float) ((pos.x * tileSize) - calculateActualMargin()),
                0f,
                (float) ((pos.y * tileSize) - calculateActualMargin()));
            //Add Components
            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            children[pos] = go;
            renderer.material = meshMaterial;
            filter.mesh = data.mesh;
        }
        public void DataAvailable(StageIO res){
            GeneratorData d = (GeneratorData) res;
            TileRequest req = activeTiles[d.uuid];
            MeshStageData mData = new MeshStageData {
                uuid = d.uuid,
                resolution = d.resolution,
                tileHeight = tileHeight,
                tileSize = tileSize + (2 * calculateActualMargin()),
                marginPix = calcMarginPix(),
                data = d.data
            };
            // Create new Mesh Target at proper position
            CreateChildMesh(req.pos, ref mData);
            // Enqueue Work
            meshPipeline.Enqueue(mData, MeshComplete);
        }

        public void MeshComplete(StageIO res){
            MeshStageData d = (MeshStageData) res;
            // should be prebaked in a stage in the mesh pipeline.
            if(jobBakesCollider){
                TileRequest req = activeTiles[d.uuid];
                children[req.pos].AddComponent<MeshCollider>();
            }
            activeTiles.Remove(d.uuid);
            isRunning = false;
            Debug.Log($"{d.uuid} mesh complete");
        }

        public void OnDestroy(){
            backingData.Dispose();
        }
    }
}