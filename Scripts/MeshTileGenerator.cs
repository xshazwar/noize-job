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

using xshazwar.noize;
using xshazwar.noize.geologic;
using xshazwar.noize.mesh;
using xshazwar.noize.pipeline;
using xshazwar.noize.scripts;
using xshazwar.noize.tile;

namespace xshazwar.noize.scripts {

    [AddComponentMenu("Noize/MeshTileGenerator", 0)]
    public class MeshTileGenerator : MonoBehaviour {
        
        public BasePipeline dataSource;
        public BasePipeline meshPipeline;
        public MeshBakery bakery;
        public string activeSaveName;
        public string activeSaveVersion;

        public TileSetMeta tileMeta;
        public ErosionSettings erosionSettings;

        public int GenTileOffsetX = 0;
        public int GenTileOffsetZ = 0;
        
        public int tileHeight = 1000;
        public int tileSize = 1000;

        public MeshType meshType = MeshType.OvershootSquareGridHeightMap;

        public int generatorResolution = 1000;
        public int tileResolution = 1000;
        public int meshResolution = 1000;
        // public int meshResolution {
        //     get {
        //         return calcTotalResolution();
        //     }
        //     private set {}
        // }

        public int margin = 5;
        public bool bakeMeshes;

        protected Dictionary<string, TileRequest> activeTiles;
        protected Dictionary<string, GameObject> children;
        
        [SerializeField]
        private List<TileRequest> _workQueue;
        
        [SerializeField]
        private List<MeshStageData> _bakeQueue;

        public ConcurrentQueue<TileRequest> workQueue;
        public ConcurrentQueue<MeshStageData> bakeQueue;

        public PipelineStateManager pipelineManager;

        protected NativeArray<float> backingData;

        public Material meshMaterial;
        public Material poolMaterial;
        public Material streamMaterial;
        public Action<StageIO> upstreamMesh;

        protected bool isRunning;
        void OnEnable()
        {
            pipelineManager = FindObjectsOfType<PipelineStateManager>()[0];
            pipelineManager.SetSavePath(activeSaveName, activeSaveVersion);
            // Init PM w/ proper context
            isRunning = false;
            activeTiles = new Dictionary<string, TileRequest>();
            workQueue = new ConcurrentQueue<TileRequest>();
            children = new Dictionary<string, GameObject>();
            backingData =  pipelineManager.GetBuffer<float, NativeArray<float>>("__MeshGenerator", generatorResolution*generatorResolution);
            NativeReference<TileSetMeta> tileMetaRef = pipelineManager.GetBuffer<TileSetMeta, NativeReference<TileSetMeta>>("__G_TileSetMeta");
            // Set from locals
            float patchRes = (tileResolution * 1f) / (tileSize * 1f);
            tileMetaRef.Value = new TileSetMeta {
                TILE_RES = new int2(meshResolution, meshResolution),
                TILE_SIZE = new int2(tileSize, tileSize),
                GENERATOR_RES = new int2(generatorResolution, generatorResolution),
                PATCH_RES = new float2(patchRes, patchRes),
                HEIGHT = tileHeight,
                HEIGHT_F = tileHeight * 1f,
                MARGIN = margin,
                // MESH_TYPE = (int)meshType,

            };
            tileMeta = tileMetaRef.Value;
            // Save back to disk
            pipelineManager.SaveBufferToDisk<TileSetMeta, NativeReference<TileSetMeta>>("__G_TileSetMeta");

            if(bakeMeshes){
                bakery = GetComponent<MeshBakery>();
            }
            AfterEnable();
        }

        protected virtual void AfterEnable(){}

        public void OnValidate(){
            // if (calcTotalResolution() > generatorResolution){
            //     throw new Exception("Generator data must have higher resolution than tile + margin");
            // }
        }

        public void Update(){
            OnUpdate();
            if(!isRunning && dataSource.pipeLineReady){
                // TODO NULLPOINTER
                if (workQueue == null){
                    Debug.Log("WorkQueue is null!");
                    return;
                }
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

        protected virtual void OnUpdate(){}

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
        public void Enqueue(string id, Vector2Int posIn){
            Vector2Int pos = new Vector2Int(posIn.x + GenTileOffsetX, posIn.y + GenTileOffsetZ);
            string key = pos.ToString();
            if (children.ContainsKey(key)){
                throw new Exception("Child exists at this position");
            }
            Debug.Log($"Enqueued {id}");
            workQueue.Enqueue(new TileRequest{
                uuid = key,
                pos = pos
            });
        }
        // protected virtual int calcTotalResolution(){
        //     double patchRes = (tileResolution * 1.0) / tileSize;
        //     return tileResolution + (2 * (int) (float) (margin * patchRes));
        // }

        // protected virtual int calcMarginVerts(){
        //     return (int) ((calcTotalResolution() - tileResolution) / 2);
        // }

        // protected virtual float calculateMarginWS(){
        //     return calcMarginVerts() * (float) ((tileSize * 1.0) / tileResolution);
        // }

        protected virtual float calculateMarginWS(){
            return margin * (float) ((tileSize * 1.0) / tileResolution);
        }

        protected virtual void OnRequestTileData(TileRequest req){}

        protected virtual void RequestTileData(TileRequest req){
            OnRequestTileData(req);
            Debug.Log($"requesting data for {req.uuid} ({req.pos.x}, {req.pos.y})");
            dataSource.Enqueue(
                new GeneratorData {
                    uuid = req.uuid,
                    resolution = generatorResolution,
                    xpos = tileResolution *  req.pos.x,
                    zpos = tileResolution *  req.pos.y,
                    data = new NativeSlice<float>(backingData)
                }
            );
            RequestMesh(req);
        }

        public void RequestMesh(TileRequest req){
            MeshStageData mData = new MeshStageData {
                uuid = req.uuid,
                inputResolution = generatorResolution,
                resolution = meshResolution,
                tileHeight = tileHeight,
                tileSize = tileSize + (2 * calculateMarginWS()),
                // marginPix = calcMarginVerts(),
                marginPix = margin,
                meshType = meshType,
                xpos = tileResolution * req.pos.x,
                zpos = tileResolution * req.pos.y,
            };
            // Create new Mesh Target at proper position
            CreateChildMesh(req.pos, ref mData);
            // Enqueue Work
            // meshPipeline.Enqueue(mData, completeAction: MeshComplete);
        }

        protected virtual void CreateChildMesh(Vector2Int pos, ref MeshStageData data){
            GameObject go = new GameObject(pos.ToString());
            go.transform.parent = this.gameObject.transform;
            go.transform.position = new Vector3(
                (float) ((pos.x * tileSize) - calculateMarginWS()),
                0f,
                (float) ((pos.y * tileSize) - calculateMarginWS()));
            //Add Components
            LiveErosion erosion = go.AddComponent<LiveErosion>();
            erosion.meshType = meshType;
            erosion.SetFromTileGenerator(
                activeTiles[data.uuid], this //, data.mesh
            );
            string key = pos.ToString();
            children[key] = go;
            activeTiles.Remove(data.uuid);
            isRunning = false;
        }


        protected virtual void OnMeshComplete(MeshStageData d){}
    
        // public void MeshBaked(string uuid){
        //     TileRequest req = activeTiles[uuid];
        //     string key = req.pos.ToString();
        //     children[key].AddComponent<MeshCollider>();
        //     OnMeshBaked(uuid);
        // }

        protected virtual void OnMeshBaked(string uuid){}

        public virtual void OnDestroy(){}
    }
}
