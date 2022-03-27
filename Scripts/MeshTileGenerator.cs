using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;

using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using static Unity.Mathematics.math;
using Unity.Mathematics;

using xshazwar.noize.pipeline;
using xshazwar.noize;
using xshazwar.noize.scripts;
using xshazwar.noize.mesh;

namespace xshazwar.noize.scripts {

    public class TileRequest{
        public string uuid;
        public Vector2Int pos;
    }

    [AddComponentMenu("Noize/MeshTileGenerator", 0)]
    public class MeshTileGenerator : MonoBehaviour {
        
        public GeneratorPipeline dataSource;
        public GeneratorPipeline meshPipeline;
        public MeshBakery bakery;

        public int tileHeight = 1000;
        public int tileSize = 1000;

        public int generatorResolution = 1000;
        public int tileResolution = 1000;
        public int margin = 5;
        public bool bakeMeshes;

        protected Dictionary<string, TileRequest> activeTiles;
        protected Dictionary<string, GameObject> children;
        public ConcurrentQueue<TileRequest> workQueue;
        public ConcurrentQueue<MeshStageData> bakeQueue;

        protected NativeArray<float> backingData;

        public Material meshMaterial;

        public Action<StageIO> upstreamData;
        public Action<StageIO> upstreamMesh;

        protected bool isRunning;
        void Awake()
        {
            isRunning = false;
            upstreamData += DataAvailable;
            upstreamMesh += MeshComplete;
            activeTiles = new Dictionary<string, TileRequest>();
            workQueue = new ConcurrentQueue<TileRequest>();
            children = new Dictionary<string, GameObject>();
            backingData = new NativeArray<float>(generatorResolution * generatorResolution, Allocator.Persistent);
            if(bakeMeshes){
                bakery = GetComponent<MeshBakery>();
            }
            AfterAwake();
        }

        protected virtual void AfterAwake(){}

        public void OnValidate(){
            if (calcTotalResolution() > generatorResolution){
                throw new Exception("Generator data must have higher resolution than tile + margin");
            }
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
        }

        public void Remove(Vector2Int pos){
            string key = pos.ToString();
            if (!children.ContainsKey(key)){
                throw new Exception("No child exists at this position");
            }
            OnBeforeRemove(key);
            Destroy(children[key]);
            children.Remove(key);
            Debug.Log($"Removed Child at {key}");
        }

        protected virtual void OnBeforeRemove(string key){}
        public void Enqueue(string id, Vector2Int pos){
            string key = pos.ToString();
            if (children.ContainsKey(key)){
                throw new Exception("Child exists at this position");
            }
            Debug.Log($"Enqueued {id}");
            workQueue.Enqueue(new TileRequest{
                uuid = id,
                pos = pos
            });
        }
        protected virtual int calcTotalResolution(){
            double patchRes = (tileResolution * 1.0) / tileSize;
            // Debug.LogWarning(patchRes);
            return tileResolution + (2 * (int) Mathf.Ceil((float) (margin * patchRes)));
        }

        protected virtual int calcMarginVerts(){
            return (int) ((calcTotalResolution() - tileResolution) / 2);
        }

        protected virtual float calculateMarginWS(){
            return calcMarginVerts() * (float) ((tileSize * 1.0) / tileResolution);
        }

        protected virtual void RequestTileData(TileRequest req){
            Debug.Log($"requesting data for {req.uuid}");
            dataSource.Enqueue(
                new GeneratorData {
                    uuid = req.uuid,
                    // resolution = calcTotalResolution(),
                    resolution = generatorResolution,
                    xpos = tileResolution *  req.pos.x,
                    zpos = tileResolution *  req.pos.y,
                    data = new NativeSlice<float>(backingData)
                },
                upstreamData
            );
        }

        protected virtual void CreateChildMesh(Vector2Int pos, ref MeshStageData data){
            data.mesh = new Mesh();
            GameObject go = new GameObject(pos.ToString());
            go.transform.parent = this.gameObject.transform;
            go.transform.position = new Vector3(
                (float) ((pos.x * tileSize) - calculateMarginWS()),
                0f,
                (float) ((pos.y * tileSize) - calculateMarginWS()));
            //Add Components
            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            string key = pos.ToString();
            children[key] = go;
            if(meshMaterial != null){
                renderer.material = meshMaterial;
            }
            // renderer.material.EnableKeyword("_EMISSION");
            filter.mesh = data.mesh;
        }
        public void DataAvailable(StageIO res){
            GeneratorData d = (GeneratorData) res;
            TileRequest req = activeTiles[d.uuid];
            MeshStageData mData = new MeshStageData {
                uuid = d.uuid,
                inputResolution = d.resolution,
                resolution = tileResolution + (2 * calcMarginVerts()),
                tileHeight = tileHeight,
                tileSize = tileSize + (2 * calculateMarginWS()),
                marginPix = calcMarginVerts(),
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
            if (!bakeMeshes || bakery == null){
                activeTiles.Remove(d.uuid);
                isRunning = false;
                return;
            }
            bakery.Enqueue(new MeshBakeOrder{
                uuid = d.uuid,
                meshID = d.mesh.GetInstanceID(),
                onCompleteBake = (string uuid) => MeshBaked(uuid)
            });
            isRunning = false;     
        }

        public void MeshBaked(string uuid){
            TileRequest req = activeTiles[uuid];
            string key = req.pos.ToString();
            children[key].AddComponent<MeshCollider>();
        }

        public virtual void OnDestroy(){
            backingData.Dispose();
        }
    }
}
