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

namespace xshazwar.noize.geologic {
    
    [AddComponentMenu("Noize/LiveErosion", 0)]
    public class LiveErosion : MonoBehaviour {
        
        // Tile Data
        private TileRequest tileData;
        public int tileHeight {get; private set;}
        public int tileSize {get; private set;}
        public int generatorResolution {get; private set;}
        public int tileResolution {get; private set;}
        public int meshResolution {get; private set;}
        public int marginRes {
            get { return (int) (generatorResolution - meshResolution) / 2 ; }
            private set {}
        }

        // Erosion Settings
        public ErosionSettings erosionSettings;

        //    State and Job Control
        
        public StandAloneJobHandler erosionJobCtl;
        public PipelineStateManager stateManager;

        // Data
        
        private NativeArray<float> debugViz;
        // private NativeArray<float> tmp;
        public NativeArray<float> poolMap {get; private set;}
        public NativeArray<float> streamMap {get; private set;}
        public NativeArray<float> particleTrack;
        public NativeArray<float> originalHeightMap {get; private set;}
        public NativeArray<float> heightMap {get; private set;}
        // 
        private int MAX_EVTS_PARTICLE = 100 + 2;
        private int QUEUE_SIZE = 0;

        private NativeList<BeyerParticle> particles;
        private NativeQueue<BeyerParticle> particleQueue;

        private NativeParallelMultiHashMap<int, ErosiveEvent> events;
        private NativeQueue<ErosiveEvent> erosions;
        private int EVENT_LIMIT = 1500;
        
        // Ready Flags
        public bool paramsReady = false;
        public bool ready = false;
        public bool updateContinuous = false;
        public bool updateSingle = false;
        public bool resetLand = false;
        public bool resetWater = false;
        [SerializeField]
        public bool performErosion = true;
        [SerializeField]
        public bool drawPools = true;


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
        private MaterialPropertyBlock streamMatProps;
        private ComputeBuffer poolBuffer;
        private ComputeBuffer streamBuffer;
        private ComputeBuffer heightBuffer;
        private ComputeBuffer argsBuffer;
        private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        private Bounds bounds;

    #if UNITY_EDITOR
        private enum CHANNEL
        {
            R = 0,
            G = 4,
            B = 8,
            A = 12
        }

        public enum MapType {
            HEIGHT,
            STREAM,
            POOL
        }

        public bool updateTexture = true;
        public MapType showMap = MapType.STREAM;
        public Texture2D texture;

        void CreateTexture(){
            texture = new Texture2D(generatorResolution, generatorResolution, TextureFormat.RGBAFloat, false);
        }

        void ApplyTexture(){
            foreach (CHANNEL c in new CHANNEL[] {CHANNEL.G, CHANNEL.B, CHANNEL.R}){ //, CHANNEL.R
                NativeSlice<float> CS = new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>((int) c);
                if(showMap == MapType.HEIGHT){
                    CS.CopyFrom(new NativeSlice<float>(heightMap));
                }else if(showMap == MapType.STREAM){
                    CS.CopyFrom(new NativeSlice<float>(streamMap));
                }else if(showMap == MapType.POOL){
                    CS.CopyFrom(new NativeSlice<float>(poolMap));
                }
                
            }
            texture.Apply();
        }
    #endif
    
        public string getBufferName(string contextAlias){
            string buffer = $"{tileData.pos.x * meshResolution}_{tileData.pos.y * meshResolution}__{generatorResolution}__{contextAlias}";
            Debug.Log(buffer);
            return buffer;
        }

        public void SetFromTileGenerator(TileRequest request, MeshTileGenerator generator, Mesh landMesh){
            double patchRes = (tileResolution * 1.0) / tileSize;
            
            this.tileData = request;
            this.bakery = generator.bakery;
            this.stateManager = generator.pipelineManager;
            this.tileHeight = generator.tileHeight;
            this.tileSize = generator.tileSize;
            this.generatorResolution = generator.generatorResolution;
            this.tileResolution = generator.tileResolution;
            this.meshResolution = generator.meshResolution;
            this.poolMaterial = generator.poolMaterial;
            this.streamMaterial = generator.streamMaterial;
            this.landMesh = landMesh;
            this.erosionSettings = generator.erosionSettings;
            Debug.Log($"{request.uuid} has LiveErosion");
            paramsReady = true;

        }

        public bool CheckDepends(){
            bool[] notReady = new bool[] {
                !stateManager.BufferExists<NativeArray<float>>(getBufferName("TERRAIN_HEIGHT")),
                stateManager.IsLocked<NativeArray<float>>(getBufferName("TERRAIN_HEIGHT")),
            };
            if(notReady.Contains<bool>(true)){
                Debug.Log($"LiveErosion! :  {String.Join(", ", notReady)}");
                return false;
            }
            Debug.Log("PoolDrawer Depends ok!");
            return true;
        }

        public void Setup(){
            if(!paramsReady || !CheckDepends()){
                return;
            }
            Debug.Log("started setup");

            erosionJobCtl = new StandAloneJobHandler();

            debugViz = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_DEBUG"), generatorResolution * generatorResolution);
            // tmp = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_TMP"), generatorResolution * generatorResolution);

            // precalculated buffers
            originalHeightMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT"));
            heightMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT_COPY"), originalHeightMap.Length);
            ResetHeightMap();

            QUEUE_SIZE = erosionSettings.PARTICLES_PER_CYCLE;
            particles = stateManager.GetBuffer<BeyerParticle, NativeList<BeyerParticle>>(getBufferName("PARTERO_PARTICLE_QUEUE_MATERIALIZE"), QUEUE_SIZE);
            particleQueue = stateManager.GetBuffer<BeyerParticle, NativeQueue<BeyerParticle>>(getBufferName("PARTERO_PARTICLE_QUEUE"), QUEUE_SIZE);
            // 
            // much larger event queue
            // private NativeParallelMultiHashMap<int, ErosiveEvent> MAX_EVTS_PARTICLE * QUEUE_SIZE
            events = stateManager.GetBuffer<int, ErosiveEvent, NativeParallelMultiHashMap<int, ErosiveEvent>>(getBufferName("PARTERO_EVT_EROSIVE"), MAX_EVTS_PARTICLE * QUEUE_SIZE);
            erosions = stateManager.GetBuffer<ErosiveEvent, NativeQueue<ErosiveEvent>>(getBufferName("PARTERO_EVT_SEDIMENT"), generatorResolution * generatorResolution);  // queue size is unrestricted, but we limit this in the job            

            particleTrack = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_PARTICLE_TRACK"), generatorResolution * generatorResolution, NativeArrayOptions.ClearMemory);
            streamMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_WATERMAP_STREAM"), generatorResolution * generatorResolution, NativeArrayOptions.ClearMemory);
            poolMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_WATERMAP_POOL"), generatorResolution * generatorResolution, NativeArrayOptions.ClearMemory); // doesn't need clear
            
            poolBuffer = new ComputeBuffer(heightMap.Length, 4); // sizeof(float)
            heightBuffer = new ComputeBuffer(heightMap.Length, 4); // sizeof(float)
            streamBuffer = new ComputeBuffer(heightMap.Length, 4); // sizeof(float)
            argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            
            poolMatProps = new MaterialPropertyBlock();
            poolMatProps.SetBuffer("_WaterValues", poolBuffer);
            poolMatProps.SetBuffer("_TerrainValues", heightBuffer);
            poolMatProps.SetFloat("_Height", tileHeight);
            poolMatProps.SetFloat("_Mesh_Size", tileSize);
            poolMatProps.SetFloat("_Mesh_Res", meshResolution * 1.0f);
            poolMatProps.SetFloat("_Data_Res", generatorResolution * 1.0f);

            streamMatProps = new MaterialPropertyBlock();
            streamMatProps.SetBuffer("_WaterValues", streamBuffer);
            streamMatProps.SetBuffer("_TerrainValues", heightBuffer);
            streamMatProps.SetFloat("_Height", tileHeight);
            streamMatProps.SetFloat("_Mesh_Size", tileSize);
            streamMatProps.SetFloat("_Mesh_Res", meshResolution * 1.0f);
            streamMatProps.SetFloat("_Data_Res", generatorResolution * 1.0f);

            bounds = new Bounds(transform.position, new Vector3(10000, 10000, 10000));
            waterMesh = MeshHelper.SquarePlanarMesh(meshResolution, tileHeight, tileSize);
            args[0] = (uint)waterMesh.GetIndexCount(0);
            args[1] = (uint)1;
            argsBuffer.SetData(args);
            #if UNITY_EDITOR
            CreateTexture();
            #endif
            ready = true;
            ApplyTexture();
            Debug.Log("LiveErosion Ready!");
        }
        
        private void ResetHeightMap(){
            NativeArray<float>.Copy(originalHeightMap, heightMap);
            ResetWaterMaps();
        }

        private void ResetWaterMaps(){
            ClearArray<float>(streamMap);
            ClearArray<float>(poolMap);
            try{
                PushBuffer();
            }catch{}
        }

        unsafe void ClearArray<T>(NativeArray<T> to_clear) where T : struct
        {
            int length = to_clear.Length;
            UnsafeUtility.MemClear(
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(to_clear),
                UnsafeUtility.SizeOf<T>() * length);
        }



        public void PushBuffer(){
            streamBuffer.SetData(streamMap);
            poolBuffer.SetData(poolMap);
            heightBuffer.SetData(heightMap);
        }

        public JobHandle ScheduleMeshUpdate(JobHandle dep){
            meshDataArray = Mesh.AllocateWritableMeshData(1);
			meshData = meshDataArray[0];
			return HeightMapMeshJob<OvershootSquareGridHeightMap, PositionStream32>.ScheduleParallel(
                landMesh,
                meshData,
                meshResolution,
                generatorResolution,
                marginRes,
                tileHeight,
                tileSize,
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

        public void Update(){
            if(!ready){
                Setup();
                return;
            }else if(resetLand){
                ResetHeightMap();
                resetLand = false;
            }else if(resetWater){
                ResetWaterMaps();
                resetWater = false;
            }
            else if(erosionJobCtl.JobComplete()){
                erosionJobCtl.CloseJob();
                if(performErosion){
                    ApplyMeshUpdate();
                    BakeUpdatedMesh();
                }
                PushBuffer();
                #if UNITY_EDITOR
                if(updateTexture){
                    ApplyTexture();
                }
                #endif
            }else if((updateContinuous || updateSingle) && !erosionJobCtl.isRunning){
                updateSingle = false;
                TriggerQueuedBeyerMT();
            }
            DrawWater();
        }

        public void TriggerQueuedBeyerMT(){
            JobHandle handle = default;
            NativeSlice<float> heightSlice = new NativeSlice<float>(heightMap);
            // NativeSlice<float> tmpSlice = new  NativeSlice<float>(tmp);
            ErosionParameters erosionParams = erosionSettings.AsParameters(generatorResolution, generatorResolution, tileHeight, tileSize);
            if(performErosion){
                for (int i = 0; i < erosionSettings.CYCLES; i++){
                    if(erosionSettings.ENABLE_THERMAL){
                        handle = ThermalErosionFilter.Schedule(heightSlice, erosionSettings.TALUS, erosionSettings.THERMAL_STEP, tileSize / tileHeight, erosionSettings.THERMAL_CYCLES, generatorResolution, handle);
                    }
                    handle = JobHandle.CombineDependencies(
                            FillBeyerQueueJob.ScheduleParallel(particleQueue, erosionParams, generatorResolution, QUEUE_SIZE, handle),
                            ClearQueueJob<ErosiveEvent>.ScheduleRun(erosions, handle),
                            ClearMultiDict<int, ErosiveEvent>.ScheduleRun(events, handle));
                    handle = CopyBeyerQueueJob.ScheduleRun(particles, particleQueue, handle);
                    handle = QueuedBeyerCycleMultiThreadJob.ScheduleParallel(heightMap, poolMap, streamMap, particleTrack, particles, events, erosionParams, EVENT_LIMIT, generatorResolution, handle);
                    handle = ProcessBeyerErosiveEventsJob.ScheduleRun(heightMap, poolMap, streamMap, particleTrack, erosions, events, erosionParams, generatorResolution, handle);
                    handle = JobHandle.CombineDependencies(
                        ClearQueueJob<BeyerParticle>.ScheduleRun(particleQueue, handle),
                        ErodeHeightMaps.ScheduleRun(heightMap, erosions, erosionParams, generatorResolution, handle),
                        UpdateFlowFromTrackJob.Schedule(poolMap, streamMap, particleTrack, erosionParams, generatorResolution, handle)
                    );
                    handle = PoolAutomataJob.Schedule(poolMap, heightMap, particleQueue, erosionParams, erosionSettings.WATER_STEPS, generatorResolution, performErosion, handle);
                }
                    
            }
            // // handle = GaussFilter.Schedule(heightSlice, tmpSlice, 3, GaussSigma.s1d50, generatorResolution, handle);
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

        public void OnDestroy(){
            argsBuffer.Release();
            poolBuffer.Release();
            heightBuffer.Release();
            streamBuffer.Release();
        }


    }
}
