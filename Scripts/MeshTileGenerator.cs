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

    // TODO move to Tile Namespace

    // public class TileRequest{
    //     public string uuid;
    //     public Vector2Int pos;
    // }

    // public struct TileSetMeta {
    //     public int generatorResolution;
    //     public int tileResolution;
    //     public int margin;
    //     public float patchRes;
    // }

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

        public int generatorResolution = 1000;
        public int tileResolution = 1000;
        public int meshResolution {
            get {
                return calcTotalResolution();
            }
            private set {}
        }

        public int margin = 5;
        public bool bakeMeshes;

        protected Dictionary<string, TileRequest> activeTiles;
        protected Dictionary<string, GameObject> children;
        public ConcurrentQueue<TileRequest> workQueue;
        public ConcurrentQueue<MeshStageData> bakeQueue;

        public PipelineStateManager pipelineManager;

        protected NativeArray<float> backingData;

        public Material meshMaterial;
        public Material poolMaterial;
        public Material streamMaterial;
        public Action<StageIO> upstreamMesh;

        protected bool isRunning;
        void Awake()
        {
            pipelineManager = FindObjectsOfType<PipelineStateManager>()[0];
            pipelineManager.SetSavePath(activeSaveName, activeSaveVersion);
            // Init PM w/ proper context
            isRunning = false;
            // upstreamMesh += MeshComplete;
            activeTiles = new Dictionary<string, TileRequest>();
            workQueue = new ConcurrentQueue<TileRequest>();
            children = new Dictionary<string, GameObject>();
            backingData =  pipelineManager.GetBuffer<float, NativeArray<float>>("__MeshGenerator", generatorResolution*generatorResolution);
            NativeReference<TileSetMeta> tileMetaRef = pipelineManager.GetBuffer<TileSetMeta, NativeReference<TileSetMeta>>("__G_TileSetMeta");
            // Set from locals
            float patchRes = (tileResolution * 1f) / (tileSize * 1f);
            tileMetaRef.Value = new TileSetMeta {
                TILE_RES = new int2(tileResolution, tileResolution),
                TILE_SIZE = new int2(tileSize, tileSize),
                GENERATOR_RES = new int2(generatorResolution, generatorResolution),
                PATCH_RES = new float2(patchRes, patchRes),
                HEIGHT = tileHeight,
                HEIGHT_F = tileHeight * 1f,
                MARGIN = margin
            };
            tileMeta = tileMetaRef.Value;
            // Save back to disk
            pipelineManager.SaveBufferToDisk<TileSetMeta, NativeReference<TileSetMeta>>("__G_TileSetMeta");

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
            OnUpdate();
            if(!isRunning && dataSource.pipeLineReady){
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
        protected virtual int calcTotalResolution(){
            double patchRes = (tileResolution * 1.0) / tileSize;
            return tileResolution + (2 * (int) (float) (margin * patchRes));
        }

        protected virtual int calcMarginVerts(){
            return (int) ((calcTotalResolution() - tileResolution) / 2);
        }

        protected virtual float calculateMarginWS(){
            return calcMarginVerts() * (float) ((tileSize * 1.0) / tileResolution);
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
                marginPix = calcMarginVerts(),
                xpos = tileResolution * req.pos.x,
                zpos = tileResolution * req.pos.y
            };
            // Create new Mesh Target at proper position
            CreateChildMesh(req.pos, ref mData);
            // Enqueue Work
            // meshPipeline.Enqueue(mData, completeAction: MeshComplete);
        }

        protected virtual void CreateChildMesh(Vector2Int pos, ref MeshStageData data){
            // data.mesh = new Mesh();
            GameObject go = new GameObject(pos.ToString());
            go.transform.parent = this.gameObject.transform;
            go.transform.position = new Vector3(
                (float) ((pos.x * tileSize) - calculateMarginWS()),
                0f,
                (float) ((pos.y * tileSize) - calculateMarginWS()));
            //Add Components
            // MeshFilter filter = go.AddComponent<MeshFilter>();
            // MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            LiveErosion erosion = go.AddComponent<LiveErosion>();
            // Rigidbody rb = go.AddComponent<Rigidbody>();
            // StreamDrawer sd = go.AddComponent<StreamDrawer>();
            // rb.isKinematic = true;
            erosion.SetFromTileGenerator(
                activeTiles[data.uuid], this //, data.mesh
            );
            string key = pos.ToString();
            // sd.referenceMat = meshMaterial;
            children[key] = go;
            // if(meshMaterial != null){
            //     Material clone = new Material(meshMaterial);
            //     clone.CopyPropertiesFromMaterial(meshMaterial);
            //     renderer.material = clone;
            //     // renderer.material = meshMaterial;
            // }
            // filter.mesh = data.mesh;
            activeTiles.Remove(data.uuid);
            isRunning = false;
        }

        // public void MeshComplete(StageIO res){
        //     MeshStageData d = (MeshStageData) res;
        //     // should be prebaked in a stage in the mesh pipeline.
        //     if (!bakeMeshes || bakery == null){
        //         activeTiles.Remove(d.uuid);
        //         isRunning = false;
        //         return;
        //     }
        //     bakery.Enqueue(new MeshBakeOrder{
        //         uuid = d.uuid,
        //         meshID = d.mesh.GetInstanceID(),
        //         onCompleteBake = (string uuid) => MeshBaked(uuid)
        //     });
        //     isRunning = false;
        //     OnMeshComplete(d);   
        // }

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
