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

        private Dictionary<string, TileRequest> activeTiles;
        private Dictionary<Vector2Int, GameObject> children;
        public ConcurrentQueue<TileRequest> workQueue;
        public ConcurrentQueue<MeshStageData> bakeQueue;

        private NativeArray<float> backingData;

        public Material meshMaterial;

        public Vector2Int target;

        public Action<StageIO> upstreamData;
        public Action<StageIO> upstreamMesh;

        private bool isRunning;
        void Awake()
        {
            isRunning = false;
            upstreamData += DataAvailable;
            upstreamMesh += MeshComplete;
            activeTiles = new Dictionary<string, TileRequest>();
            workQueue = new ConcurrentQueue<TileRequest>();
            children = new Dictionary<Vector2Int, GameObject>();
            backingData = new NativeArray<float>(generatorResolution * generatorResolution, Allocator.Persistent);
            if(bakeMeshes){
                bakery = GetComponent<MeshBakery>();
            }
        }

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
            if (!children.ContainsKey(pos)){
                throw new Exception("No child exists at this position");
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
            double patchRes = (tileResolution * 1.0) / tileSize;
            // Debug.LogWarning(patchRes);
            return tileResolution + (2 * (int) Mathf.Ceil((float) (margin * patchRes)));
        }

        private int calcMarginVerts(){
            return (int) ((calcTotalResolution() - tileResolution) / 2);
        }

        private float calculateMarginWS(){
            return calcMarginVerts() * (float) ((tileSize * 1.0) / tileResolution);
        }

        private void RequestTileData(TileRequest req){
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

        private void CreateChildMesh(Vector2Int pos, ref MeshStageData data){
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
            children[pos] = go;
            renderer.material = meshMaterial;
            renderer.material.EnableKeyword("_EMISSION");
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
            children[req.pos].AddComponent<MeshCollider>();
        }

        public void OnDestroy(){
            backingData.Dispose();
        }
    }
}
