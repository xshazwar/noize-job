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

        //    State and Job Control
        
        public StandAloneJobHandler erosionJobCtl;
        public PipelineStateManager stateManager;

        // Data
        
        private NativeArray<float> debugViz;
        private NativeArray<float> tmp;
        public NativeArray<float> poolMap {get; private set;}
        public NativeArray<float> streamMap {get; private set;}
        public NativeArray<float> particleTrack;
        public NativeArray<float> originalHeightMap {get; private set;}
        public NativeArray<float> heightMap {get; private set;}
        private NativeArray<BeyerParticle> beyerParticles;
        // 
        private int MAX_EVTS_PARTICLE = 100 + 2;
        private int QUEUE_SIZE = 1000;
        private NativeList<BeyerParticle> particles;
        private NativeQueue<BeyerParticle> particleQueue;

        private NativeQueue<ErosiveEvent> events;
        
        // Ready Flags
        public bool paramsReady = false;
        public bool ready = false;
        public bool updateContinuous = false;
        public bool updateSingle = false;
        public bool resetLand = false;
        public bool performErosion = true;
        
        // Erosion Control

        public bool thermalErosion = false;
        public float talusAngle = 45f;
        public float thermalStepSize = 0.1f;
        public int thermalCyclesPerCycle = 3;

        const int PARTICLE_COUNT = 10; // maybe leave some overhead threads for other jobs to run during erosion? Remeshing comes to mind
        public int EVENT_LIMIT = 1500; // event limit per particle before intermediate results are calculated. Should align to a frame or second or something...?
        public int Cycles = 1;

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

        public MapType showMap = MapType.HEIGHT;
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
            tmp = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_TMP"), generatorResolution * generatorResolution);

            // precalculated buffers
            originalHeightMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT"));
            heightMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT_COPY"), originalHeightMap.Length);
            ResetHeightMap();
        
            particles = stateManager.GetBuffer<BeyerParticle, NativeList<BeyerParticle>>(getBufferName("PARTERO_PARTICLE_QUEUE_MATERIALIZE"), QUEUE_SIZE);
            particleQueue = stateManager.GetBuffer<BeyerParticle, NativeQueue<BeyerParticle>>(getBufferName("PARTERO_PARTICLE_QUEUE"), QUEUE_SIZE);
            // 
            beyerParticles = stateManager.GetBuffer<BeyerParticle, NativeArray<BeyerParticle>>(getBufferName("PARTERO_PARTICLEBEYER_AGENT"), PARTICLE_COUNT, NativeArrayOptions.ClearMemory);

            // events = stateManager.GetBuffer<ErosiveEvent, NativeQueue<ErosiveEvent>>(getBufferName("PARTERO_EVT_EROSIVE"), EVENT_LIMIT * PARTICLE_COUNT);  // queue size is unrestricted, but we limit this in the job            
            // much larger event queue
            events = stateManager.GetBuffer<ErosiveEvent, NativeQueue<ErosiveEvent>>(getBufferName("PARTERO_EVT_EROSIVE"), MAX_EVTS_PARTICLE * QUEUE_SIZE);  // queue size is unrestricted, but we limit this in the job            

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
            for(int i = 0; i < beyerParticles.Length; i++){
                BeyerParticle bp = beyerParticles[i];
                bp.isDead = true;
                beyerParticles[i] = bp;
            }
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
                // onCompleteBake = (string uuid) => Debug.Log($"baked {uuid}")
            });
        }

        public void Update(){
            if(!ready){
                Setup();
                return;
            }else if(resetLand){
                ResetHeightMap();
                resetLand = false;
            }
            else if(erosionJobCtl.JobComplete()){
                erosionJobCtl.CloseJob();
                if(performErosion){
                    ApplyMeshUpdate();
                    BakeUpdatedMesh();
                }
                PushBuffer();
                #if UNITY_EDITOR
                ApplyTexture();
                #endif
            }else if((updateContinuous || updateSingle) && !erosionJobCtl.isRunning){
                updateSingle = false;
                // TriggerCycleBeyer();
                // TriggerCycleBeyerMT();
                TriggerQueuedBeyerMT();
            }
            DrawWater();
        }

        public void TriggerQueuedBeyerMT(){
            JobHandle handle = default;
            NativeSlice<float> heightSlice = new NativeSlice<float>(heightMap);
            NativeSlice<float> tmpSlice = new  NativeSlice<float>(tmp);
            for (int i = 0; i < Cycles; i++){
                if(performErosion){
                    if(thermalErosion){
                        handle = ThermalErosionFilter.Schedule(heightSlice, talusAngle, thermalStepSize, 0.75f, thermalCyclesPerCycle, generatorResolution, handle);
                    }
                    handle = FillBeyerQueueJob.ScheduleParallel(particleQueue, generatorResolution, QUEUE_SIZE, handle);
                    handle = CopyBeyerQueueJob.ScheduleRun(particles, particleQueue, handle);
                    handle = QueuedBeyerCycleMultiThreadJob.ScheduleParallel(heightMap, poolMap, streamMap, particleTrack, particles, events, EVENT_LIMIT, generatorResolution, handle);
                    handle = JobHandle.CombineDependencies(
                        ClearBeyerQueueJob.ScheduleRun(particleQueue, handle),
                        ProcessBeyerErosiveEventsJob.ScheduleRun(heightMap, poolMap, streamMap, particleTrack, events, generatorResolution, handle));
                }
                handle = UpdateFlowFromTrackJob.Schedule(poolMap, streamMap, particleTrack, generatorResolution, handle);
                handle = PoolAutomataJob.Schedule(poolMap, heightMap, particleQueue, 1, generatorResolution, handle);
            }
            // handle = GaussFilter.Schedule(heightSlice, tmpSlice, 3, GaussSigma.s1d50, generatorResolution, handle);
            if(performErosion){
                handle = ScheduleMeshUpdate(handle);
             }
            erosionJobCtl.TrackJob(handle);
        }

        // public void TriggerCycleBeyerMT(){
        //     JobHandle handle = default;
        //     NativeSlice<float> heightSlice = new NativeSlice<float>(heightMap);
        //     NativeSlice<float> tmpSlice = new  NativeSlice<float>(tmp);
        //     for (int i = 0; i < Cycles; i++){
        //         if(performErosion){
        //             if(thermalErosion){
        //                 handle = ThermalErosionFilter.Schedule(heightSlice, talusAngle, thermalStepSize, 0.75f, thermalCyclesPerCycle, generatorResolution, handle);
        //             }
        //             handle = BeyerCycleMultiThreadJob.ScheduleParallel(heightMap, poolMap, streamMap, particleTrack, beyerParticles, events, EVENT_LIMIT, generatorResolution, handle);
        //             handle = ProcessBeyerErosiveEventsJob.ScheduleRun(heightMap, poolMap, streamMap, particleTrack, events, generatorResolution, handle);
        //         }
        //         handle = UpdateFlowFromTrackJob.Schedule(poolMap, streamMap, particleTrack, generatorResolution, handle);
        //         handle = PoolAutomataJob.Schedule(poolMap, heightMap, 10, generatorResolution, handle);
        //     }
        //     // handle = GaussFilter.Schedule(heightSlice, tmpSlice, 3, GaussSigma.s1d50, generatorResolution, handle);
        //     if(performErosion){
        //         handle = ScheduleMeshUpdate(handle);
        //      }
        //     erosionJobCtl.TrackJob(handle);
        // }


        // public void TriggerCycleBeyer(){
        //     JobHandle handle = default;
        //     JobHandle cycle = default;
        //     NativeSlice<float> heightSlice = new NativeSlice<float>(heightMap);
        //     NativeSlice<float> tmpSlice = new  NativeSlice<float>(tmp);
        //     for (int i = 0; i < Cycles; i++){
        //         cycle = ErosionCycleBeyerParticleJob.ScheduleRun(heightMap, poolMap, streamMap, particleTrack, beyerParticles, events, EVENT_LIMIT, generatorResolution, handle);
        //         handle = UpdateFlowFromTrackJob.Schedule(poolMap, streamMap, particleTrack, generatorResolution, cycle);
        //         if(thermalErosion){
        //             handle = ThermalErosionFilter.Schedule(heightSlice, talusAngle, thermalStepSize, 0.75f, thermalCyclesPerCycle, generatorResolution, handle);
        //         }
        //         // handle = ThermalErosionFilter.Schedule(new NativeSlice<float>(heightMap), .001f, 0.25f, 4, generatorResolution, handle);
        //     }
        //     // handle = ThermalErosionFilter.Schedule(new NativeSlice<float>(heightMap), 30f, 0.5f, .75f, 4, generatorResolution, handle);
        //     handle = GaussFilter.Schedule(heightSlice, tmpSlice, 5, GaussSigma.s0d50, generatorResolution, handle);
        //     handle = ScheduleMeshUpdate(handle);
        //     erosionJobCtl.TrackJob(handle);
        // }

        public void DrawWater(){
            Graphics.DrawMeshInstancedIndirect(waterMesh, 0, poolMaterial, bounds, argsBuffer, 0, poolMatProps);
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
