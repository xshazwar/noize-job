using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

using static Unity.Mathematics.math;
using Unity.Mathematics;

using xshazwar.noize.pipeline;
using xshazwar.noize;
using xshazwar.noize.filter.blur;
using xshazwar.noize.scripts;
using xshazwar.noize.mesh;
using xshazwar.noize.mesh.Generators;
using xshazwar.noize.mesh.Streams;
using xshazwar.noize.tile;

namespace xshazwar.noize.geologic {
    
    [AddComponentMenu("Noize/LiveErosion", 0)]
    public class LiveErosion : MonoBehaviour, IProvideGeodata {
        
        // Tile Data
        [SerializeField]
        private TileRequest tileData;
        // [SerializeField]
        private TileSetMeta tileMeta;

        // Erosion Settings
        public ErosionSettings erosionSettings;

        //    State and Job Control
        
        public StandAloneJobHandler erosionJobCtl;
        public PipelineStateManager stateManager;

        // Terrain Texture Controls
        public Texture2D waterControl;
        public Texture2D textureControl;

        public Texture2D GetWaterControlTexture(){ return waterControl; }
        public Texture2D GetTerrainControlTexture(){ return textureControl; }

        public ColorChannelByte byteColor = ColorChannelByte.B;

        // Data
        private NativeArray<float> debugViz;
        public NativeArray<float> poolMap {get; private set;}
        public NativeArray<float> streamMap {get; private set;}
        public NativeArray<float> plantDensityMap {get; private set;}
        public NativeArray<float> particleTrack;

        public int heightMapSize = -1;
        public NativeArray<float> originalHeightMap {get; private set;}
        public NativeArray<float> heightMap {get; private set;}
        // 
        private int MAX_EVTS_PARTICLE = 0;
        private int QUEUE_SIZE = 0;
        private int particleGenerationID = 0;

        private NativeList<BeyerParticle> particles;
        private NativeQueue<BeyerParticle> particleQueue;

        private NativeParallelMultiHashMap<int, ErosiveEvent> events;
        private NativeQueue<ErosiveEvent> erosions;
        private int EVENT_LIMIT = 1500;
        
        // DEBUG
        public bool debugDescent = false;
        // Ready Flags
        public bool paramsReady = false;
        public bool buffersReady = false;
        public bool isSetup = false;
        public bool updateContinuous = false;
        public bool updateSingle = false;
        public bool resetLand = false;
        public bool resetWater = false;

        public MeshType meshType = MeshType.OvershootSquareGridHeightMap;


        [SerializeField]
        public bool performErosion = true;
        [SerializeField]
        public bool drawPools;


        // Meshing
        private MeshBakery bakery;
        private Mesh landMesh;
        private Mesh.MeshDataArray meshDataArray;
        private Mesh.MeshData meshData;
        private Mesh waterMesh;

         // Instanced Drawing
        private Material poolMaterial;
        private Material streamMaterial;
        private MaterialPropertyBlock poolMatProps;
        // private MaterialPropertyBlock streamMatProps;
        private ComputeBuffer poolBuffer;
        private ComputeBuffer streamBuffer;
        private ComputeBuffer heightBuffer;
        private ComputeBuffer argsBuffer;
        private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        private Bounds bounds;
        // events
        public Action OnGeodataReady {get; set;}
        public Action OnWaterUpdate {get; set;}
        public Action DisabledEvent {get; set;}
        public Action EnabledEvent {get; set;}

    #if UNITY_EDITOR
        
        public void SaveErosionState(){
            originalHeightMap.CopyFrom(heightMap);
            stateManager.SaveBufferToDisk<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x);
            stateManager.SaveBufferToDisk<float, NativeArray<float>>(getBufferName("PARTERO_WATERMAP_STREAM"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x);
            stateManager.SaveBufferToDisk<float, NativeArray<float>>(getBufferName("PARTERO_WATERMAP_POOL"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x); 
        }

        public enum MapType {
            HEIGHT,
            STREAM,
            POOL,
            PLANT
        }

        public bool updateTexture = true;
        public MapType showMap = MapType.STREAM;
        public Texture2D texture;

        void CreateTexture(){
            texture = new Texture2D(tileMeta.GENERATOR_RES.x, tileMeta.GENERATOR_RES.x, TextureFormat.RGBAFloat, false);
            waterControl = new Texture2D(tileMeta.TILE_RES.x, tileMeta.TILE_RES.x, TextureFormat.RGBA32, false);
            textureControl = new Texture2D(tileMeta.TILE_RES.x, tileMeta.TILE_RES.x, TextureFormat.RGBA32, false);
            SetTextureBlackJob.ScheduleRun(waterControl, (JobHandle) default).Complete();
            SetTextureBlackJob.ScheduleRun(textureControl, (JobHandle) default).Complete();
            Debug.Log($"Control Texture ready? {textureControl == null} {tileMeta.TILE_RES.x}");
        }

        void ApplyTexture(){
            foreach (ColorChannelFloat c in new ColorChannelFloat[] {ColorChannelFloat.G}){ // {ColorChannel.G, ColorChannel.B, ColorChannel.R}){
                NativeSlice<float> CS = new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>((int) c);
                if(showMap == MapType.HEIGHT){
                    CS.CopyFrom(new NativeSlice<float>(heightMap));
                }else if(showMap == MapType.STREAM){
                    CS.CopyFrom(new NativeSlice<float>(streamMap));
                }else if(showMap == MapType.POOL){
                    CS.CopyFrom(new NativeSlice<float>(poolMap));
                }else if(showMap == MapType.PLANT){
                    CS.CopyFrom(new NativeSlice<float>(plantDensityMap));
                }
            }
            texture.Apply();
            waterControl.Apply();
            textureControl.Apply();
        }
    #endif
    
        public string getBufferName(string contextAlias){
            return $"{tileData.pos.x * tileMeta.TILE_RES.x}_{tileData.pos.y * tileMeta.TILE_RES.x}__{tileMeta.GENERATOR_RES.x}__{contextAlias}";
        }

        public void SetFromTileGenerator(TileRequest request, MeshTileGenerator generator){
            this.stateManager = generator.pipelineManager;
            NativeReference<TileSetMeta> tileMetaRef = stateManager.GetBuffer<TileSetMeta, NativeReference<TileSetMeta>>("__G_TileSetMeta");
            tileMeta = tileMetaRef.Value;
            #if NJ_DBG_PARTFLOW
                Debug.Log($"{tileMeta.GENERATOR_RES.x} | {tileMeta.TILE_RES.x} | {tileMeta.TILE_SIZE.x} | {tileMeta.HEIGHT}");
            #endif
            
            this.tileData = request;
            this.bakery = generator.bakery;
            this.poolMaterial = generator.poolMaterial;
            this.erosionSettings = generator.erosionSettings;

            landMesh = new Mesh();
            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            StreamDrawer sd = gameObject.AddComponent<StreamDrawer>();
            sd.SetParams(tileMeta, generator.meshMaterial);
            filter.mesh = landMesh;
            #if NJ_DBG_PARTFLOW
                Debug.Log($"{request.uuid} has LiveErosion");
            #endif
            paramsReady = true;

        }

        public bool CheckDepends(){
            bool[] notReady = new bool[] {
                !stateManager.BufferExists<NativeArray<float>>(getBufferName("TERRAIN_HEIGHT")),
                stateManager.IsLocked<NativeArray<float>>(getBufferName("TERRAIN_HEIGHT")),
            };
            if(notReady.Contains<bool>(true)){
                #if NJ_DBG_PARTFLOW
                    Debug.Log($"LiveErosion! :  {String.Join(", ", notReady)}");
                #endif
                return false;
            }
            return true;
        }

        public void Setup(){
            if(!paramsReady || !CheckDepends()){
                return;
            }
            Debug.LogWarning("Setting up LiveErosion component.");

            debugViz = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_DEBUG"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x);

            // precalculated buffers
            originalHeightMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT"));
            heightMapSize = originalHeightMap.Length;
            heightMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT_COPY"), heightMapSize);

            #if NJ_DBG_PARTFLOW
                QUEUE_SIZE = 1;
            #else
                QUEUE_SIZE = erosionSettings.PARTICLES_PER_CYCLE;
            #endif
            

            MAX_EVTS_PARTICLE = erosionSettings.MAXAGE * 2;
            Debug.LogWarning($"particle event queue size {max(QUEUE_SIZE, 10) * MAX_EVTS_PARTICLE * 2}");
            particles = stateManager.GetBuffer<BeyerParticle, NativeList<BeyerParticle>>(getBufferName("PARTERO_PARTICLE_QUEUE_MATERIALIZE"), QUEUE_SIZE);
            particleQueue = stateManager.GetBuffer<BeyerParticle, NativeQueue<BeyerParticle>>(getBufferName("PARTERO_PARTICLE_QUEUE"), QUEUE_SIZE);
    
            events = stateManager.GetBuffer<int, ErosiveEvent, NativeParallelMultiHashMap<int, ErosiveEvent>>(getBufferName("PARTERO_EVT_EROSIVE"), MAX_EVTS_PARTICLE * QUEUE_SIZE);
            erosions = stateManager.GetBuffer<ErosiveEvent, NativeQueue<ErosiveEvent>>(getBufferName("PARTERO_EVT_SEDIMENT"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x);  // queue size is unrestricted, but we limit this in the job            

            particleTrack = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_PARTICLE_TRACK"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x, NativeArrayOptions.ClearMemory);
            streamMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_WATERMAP_STREAM"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x, NativeArrayOptions.ClearMemory);
            poolMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_WATERMAP_POOL"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x, NativeArrayOptions.ClearMemory); // doesn't need clear
            plantDensityMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_PLANTDENSITYMAP"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x, NativeArrayOptions.ClearMemory); // doesn't need clear
            

            ResetHeightMap();

            poolMatProps = new MaterialPropertyBlock();
            poolMatProps.SetBuffer("_WaterValues", poolBuffer);
            poolMatProps.SetBuffer("_TerrainValues", heightBuffer);
            poolMatProps.SetFloat("_Height", tileMeta.HEIGHT);
            poolMatProps.SetFloat("_Mesh_Size", tileMeta.TILE_SIZE.x);
            poolMatProps.SetFloat("_Mesh_Res", tileMeta.TILE_RES.x * 1.0f);
            poolMatProps.SetFloat("_Data_Res", tileMeta.GENERATOR_RES.x * 1.0f);

            bounds = new Bounds(transform.position, new Vector3(10000, 10000, 10000));
            waterMesh = MeshHelper.SquarePlanarMesh(tileMeta.TILE_RES.x, tileMeta.HEIGHT, tileMeta.TILE_SIZE.x);
            #if UNITY_EDITOR
            CreateTexture();
            #endif
            isSetup = true;
            ApplyTexture();
            OnPostInit();
        }
        public void SetBuffers(){
            if(heightMapSize < 0){
                heightMapSize = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT")).Length;
            }
            if(heightMapSize < 0){
                Debug.Log("No height map available to set buffers");
                return;
            }
            poolBuffer = new ComputeBuffer(heightMapSize, 4); // sizeof(float)
            heightBuffer = new ComputeBuffer(heightMapSize, 4); // sizeof(float)
            streamBuffer = new ComputeBuffer(heightMapSize, 4); // sizeof(float)
            argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            args[0] = (uint)waterMesh.GetIndexCount(0);
            args[1] = (uint)1;
            argsBuffer.SetData(args);
            erosionJobCtl = new StandAloneJobHandler();
            buffersReady = true;
        }

        public void ReleaseBuffers(){
            if(argsBuffer != null){
                argsBuffer.Release();
                argsBuffer = null;
                poolBuffer.Release();
                poolBuffer = null;
                heightBuffer.Release();
                heightBuffer = null;
                streamBuffer.Release();
                streamBuffer = null;
            }
            buffersReady = false;
        }

        private void OnPostInit(){
            OnGeodataReady?.Invoke();
            EnabledEvent?.Invoke();
        }
        
        private void ResetHeightMap(){
            NativeArray<float>.Copy(originalHeightMap, heightMap);
            ResetPlantMap();
            ResetWaterMaps();
        }

        private void ResetWaterMaps(){
            ClearArray<float>(streamMap);
            ClearArray<float>(poolMap);
            try{
                PushBuffer();
            }catch{}
        }

        private void ResetPlantMap(){
            ClearArray<float>(plantDensityMap);
        }

        void ClearArray<T>(NativeArray<T> to_clear) where T : struct
        {
            int length = to_clear.Length;
            unsafe {
                UnsafeUtility.MemClear(
                    NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(to_clear),
                    UnsafeUtility.SizeOf<T>() * length);
            }
            
        }

        public void PushBuffer(){
            streamBuffer.SetData(streamMap);
            poolBuffer.SetData(poolMap);
            heightBuffer.SetData(heightMap);
        }

        static HeightMapMeshJobScheduleDelegate[] jobs = {
			HeightMapMeshJob<SquareGridHeightMap, PositionStream32>.ScheduleParallel,
            HeightMapMeshJob<OvershootSquareGridHeightMap, PositionStream32>.ScheduleParallel,
            HeightMapMeshJob<FlatHexagonalGridHeightMap, PositionStream32>.ScheduleParallel
		};

        public JobHandle ScheduleMeshUpdate(JobHandle dep){
            meshDataArray = Mesh.AllocateWritableMeshData(1);
			meshData = meshDataArray[0];
            return jobs[(int)meshType](
            // return HeightMapMeshJob<FlatHexagonalGrid, PositionStream32>.ScheduleParallel(
            // return HeightMapMeshJob<OvershootSquareGridHeightMap, PositionStream32>.ScheduleParallel(
                landMesh,
                meshData,
                tileMeta.TILE_RES.x,
                tileMeta.GENERATOR_RES.x,
                tileMeta.MARGIN,
                tileMeta.HEIGHT,
                tileMeta.TILE_SIZE.x,
                new NativeSlice<float>(heightMap),
                dep);
        }

        public void ApplyMeshUpdate(){
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, landMesh,
                MeshUpdateFlags.DontNotifyMeshUsers |
                MeshUpdateFlags.DontValidateIndices |
                MeshUpdateFlags.DontRecalculateBounds);
        }

        public void BakeUpdatedMesh(){
            int meshID = landMesh.GetInstanceID();
            bakery.Enqueue(new MeshBakeOrder{
                uuid = meshID.ToString(),
                meshID = meshID
            });
        }

        private void TestResume(){
            NativeArray<int> _test = stateManager.GetBuffer<int, NativeArray<int>>("__PARTERO_RESUME_TEST", 1, NativeArrayOptions.ClearMemory);
            Debug.Log($"test value is {_test[0]}");
        }

        public void Update(){
            if(!isSetup){
                Setup();
                return;
            }
            else if(!buffersReady){
                SetBuffers();   
                TestResume();
                return;
            }
            else if(resetLand){
                ResetHeightMap();
                resetLand = false;
            }else if(resetWater){
                ResetWaterMaps();
                resetWater = false;
            }
            else if(erosionJobCtl.JobComplete()){
                particleGenerationID++;
                erosionJobCtl.CloseJob();
                #if NJ_DBG_PARTFLOW
                    ApplyMeshUpdate();
                    VisualizeParticleRun(ref events);
                #else
                    if(performErosion){
                        ApplyMeshUpdate();
                        BakeUpdatedMesh();
                    }
                    PushBuffer();
                    #if UNITY_EDITOR
                        ApplyTexture();
                    #endif
                #endif
                
                OnCompleteCycle();
            }else if((updateContinuous || updateSingle) && !erosionJobCtl.isRunning){
                updateSingle = false;
                #if NJ_DBG_PARTFLOW
                    ScheduleParticleDebugRun();
                #else
                    TriggerQueuedBeyerMT();
                #endif
            }
            DrawWater();
        }

        private void OnCompleteCycle(){
            OnWaterUpdate?.Invoke();
        }

        public void TriggerQueuedBeyerMT(){
            JobHandle handle = default;
            NativeSlice<float> heightSlice = new NativeSlice<float>(heightMap);
            ErosionParameters erosionParams = erosionSettings.AsParameters();
            if(performErosion){
                for (int i = 0; i < erosionSettings.CYCLES; i++){
                    if(erosionSettings.ENABLE_THERMAL && erosionSettings.BEHAVIOR != ErosionMode.ONLY_FLOW_WATER){
                        handle = ThermalErosionFilter.Schedule(heightSlice, erosionSettings.TALUS, erosionSettings.THERMAL_STEP, tileMeta.TILE_SIZE.x / tileMeta.HEIGHT, erosionSettings.THERMAL_CYCLES, tileMeta.GENERATOR_RES.x, handle);
                    }
                    if(erosionSettings.BEHAVIOR != ErosionMode.ONLY_FLOW_WATER){
                        handle = JobHandle.CombineDependencies(
                            FillBeyerQueueJob.ScheduleParallel(
                                particleQueue,
                                erosionParams,
                                tileMeta,
                                particleGenerationID % 4,
                                tileMeta.GENERATOR_RES.x,
                                QUEUE_SIZE,
                                handle,
                                min(10, QUEUE_SIZE)
                            ),
                            ClearQueueJob<ErosiveEvent>.ScheduleRun(erosions, handle),
                            ClearMultiDict<int, ErosiveEvent>.ScheduleRun(events, handle));
                    }else{
                        handle = JobHandle.CombineDependencies(
                            ClearQueueJob<ErosiveEvent>.ScheduleRun(erosions, handle),
                            ClearMultiDict<int, ErosiveEvent>.ScheduleRun(events, handle));
                    }
                    handle = CopyBeyerQueueJob.ScheduleRun(particles, particleQueue, handle);
                    handle = QueuedBeyerCycleMultiThreadJob.ScheduleParallel(heightMap, poolMap, streamMap, particleTrack, particles, events, erosionParams, tileMeta, EVENT_LIMIT, tileMeta.GENERATOR_RES.x, handle);
                    handle = ProcessBeyerErosiveEventsJob.ScheduleRun(heightMap, poolMap, streamMap, particleTrack, erosions, events, erosionParams, tileMeta, tileMeta.GENERATOR_RES.x, handle);
                    handle = JobHandle.CombineDependencies(
                        ClearQueueJob<BeyerParticle>.ScheduleRun(particleQueue, handle),
                        ErodeHeightMaps.ScheduleRun(heightMap, erosions, erosionParams, tileMeta, tileMeta.GENERATOR_RES.x, handle),
                        UpdateFlowFromTrackJob.Schedule(poolMap, streamMap, particleTrack, erosionParams, tileMeta, tileMeta.GENERATOR_RES.x, handle)
                    );
                    handle = PoolAutomataJob.Schedule(poolMap, heightMap, particleQueue, erosionParams, tileMeta, erosionSettings.WATER_STEPS, tileMeta.GENERATOR_RES.x, performErosion, handle);
                }  
            }
            
            // wet
            handle = SetRGBA32Job.ScheduleRun(poolMap, waterControl, ColorChannelByte.R, handle, 1000f);
            // puddle
            handle = SetRGBA32Job.ScheduleRun(poolMap, waterControl, ColorChannelByte.G, handle, 1000f);
            // stream
            handle = SetRGBA32Job.ScheduleRun(streamMap, waterControl, ColorChannelByte.B, handle, 2f);

            // cavity
            handle = SetRGBA32Job.ScheduleRun(streamMap, textureControl, ColorChannelByte.G, handle, 1f);
            handle = CurvitureMapJob.ScheduleRun(textureControl, heightMap, tileMeta, ColorChannelByte.G, tileMeta.GENERATOR_RES.x, handle);
            
            // erosion
            handle = SetRGBA32InverseJob.ScheduleRun(streamMap, textureControl, ColorChannelByte.A, handle, 3f);
            
            if(performErosion){
                handle = ScheduleMeshUpdate(handle);
             }
            erosionJobCtl.TrackJob(handle);
        }

        public void DrawWater(){
            if(drawPools){
                Graphics.DrawMeshInstancedIndirect(waterMesh, 0, poolMaterial, bounds, argsBuffer, 0, poolMatProps);
            }
            // Graphics.DrawMeshInstancedIndirect(waterMesh, 0, streamMaterial, bounds, argsBuffer, 0, streamMatProps);
        }

        public void OnDisable(){
            DisabledEvent?.Invoke();
            ReleaseBuffers();
        }

    // Primary Debug Workflow
    #if NJ_DBG_PARTFLOW

        public List<Tuple<Vector3, Vector3>> debugRun;
        public void ScheduleParticleDebugRun(){
            JobHandle handle = default;
            NativeSlice<float> heightSlice = new NativeSlice<float>(heightMap);
            ErosionParameters erosionParams = erosionSettings.AsParameters();
            handle = JobHandle.CombineDependencies(
                FillBeyerQueueJob.ScheduleParallel(
                    particleQueue,
                    erosionParams,
                    tileMeta,
                    particleGenerationID % 4,
                    tileMeta.GENERATOR_RES.x,
                    QUEUE_SIZE,
                    handle,
                    min(10, QUEUE_SIZE)
                ),
                ClearQueueJob<ErosiveEvent>.ScheduleRun(erosions, handle),
                ClearMultiDict<int, ErosiveEvent>.ScheduleRun(events, handle));
            handle = CopyBeyerQueueJob.ScheduleRun(particles, particleQueue, handle);
            handle = QueuedBeyerCycleMultiThreadJob.ScheduleParallel(heightMap, poolMap, streamMap, particleTrack, particles, events, erosionParams, tileMeta, EVENT_LIMIT, tileMeta.GENERATOR_RES.x, handle);
            handle = ScheduleMeshUpdate(handle);
            erosionJobCtl.TrackJob(handle);
        }

        public int2 GetPos(int idx){
            int posOffset = (int) (tileMeta.GENERATOR_RES.x - tileMeta.TILE_RES.x) / 2;
            int2 pos = new int2(
                (int) idx / tileMeta.GENERATOR_RES.x,
                (int) idx % tileMeta.GENERATOR_RES.y
            );
            return pos - posOffset;
        }

        public int2 GetGPos(int idx){
            return new int2(
                (int) idx / tileMeta.GENERATOR_RES.x,
                (int) idx % tileMeta.GENERATOR_RES.y
            );
        }

        public Vector3 GetWSGridPosition(int idx, ref int2 p){
            p = GetPos(idx);
            return new Vector3(
                p.x /  (float) tileMeta.TILE_RES.x * (float) tileMeta.TILE_SIZE.x,
                (heightMap[idx] * tileMeta.HEIGHT) + 5f, // should be just over map
                p.y /  (float) tileMeta.TILE_RES.y * (float) tileMeta.TILE_SIZE.y
            );
        }

        public void VisualizeParticleRun(ref NativeParallelMultiHashMap<int, ErosiveEvent> events){
            if(debugRun == null){
                debugRun = new List<Tuple<Vector3, Vector3>>();
            }else{
                debugRun.Clear();
            }
            NativeArray<ErosiveEvent> evtArr = events.GetValueArray(Allocator.Persistent);
            evtArr.Sort<ErosiveEvent, SortErosiveEventsAgeHelper>(new SortErosiveEventsAgeHelper());
            ErosiveEvent a;
            ErosiveEvent b;
            Vector3 av;
            Vector3 bv;
            int2 pa = new int2();
            int2 pb = new int2();
            int2 pag = new int2();
            for(int i = 0; i < evtArr.Length - 1; i++){
                a = evtArr[i];
                b = evtArr[i + 1];
                if(a.actor != b.actor) continue;
                pag = GetGPos(a.idx);
                av = GetWSGridPosition(a.idx, ref pa);
                bv = GetWSGridPosition(b.idx, ref pb);
                Debug.Log($"{pa.x}, {pa.y} (( -o- {pag.x}, {pag.y})) >> {pb.x}, {pb.y} a:{a.age}/{b.age} @ {a.actor}");
                debugRun.Add(Tuple.Create(av, bv));
            }
            evtArr.Dispose();
        }

        void OnDrawGizmos()
        {
            if (debugRun != null)
            {
                Gizmos.color = Color.red;
                foreach( Tuple<Vector3, Vector3> pair in debugRun){
                    Gizmos.DrawLine(pair.Item1, pair.Item2);
                }
            }
        }
    #endif
    }
}
