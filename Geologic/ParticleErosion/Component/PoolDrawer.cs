using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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

namespace xshazwar.noize.geologic {

    
    [AddComponentMenu("Noize/PoolDrawer", 0)]
    public class PoolDrawer : MonoBehaviour {
        private TileRequest tileData;
        private BasePipeline poolGenerator;
        private BasePipeline poolSolver;
        public int tileHeight {get; private set;}
        public int tileSize {get; private set;}
        public int generatorResolution {get; private set;}
        public int tileResolution {get; private set;}
        public int meshResolution {get; private set;}
        public int marginRes {
            get { return (int) (generatorResolution - meshResolution) / 2 ; }
            private set {}
        }
        private Material poolMaterial;
        private Material streamMaterial;
        private MaterialPropertyBlock poolMatProps;
        private MaterialPropertyBlock streamMatProps;
        public StandAloneJobHandler erosionJobCtl;
        public PipelineStateManager stateManager;

        private NativeArray<float> debugViz;
        public NativeArray<float> poolMap {get; private set;}
        public NativeArray<float> streamMap {get; private set;}
        public NativeArray<float> particleTrack;
        public NativeArray<float> heightMap {get; private set;}
        private NativeArray<Particle> particles;
        private NativeQueue<ErosiveEvent> events;
        public NativeQueue<PoolUpdate> poolUpdates;
        private NativeParallelMultiHashMap<PoolKey, int> drainToMinima;
        public NativeParallelHashMap<int, int> catchment {get; private set;}
        public NativeParallelHashMap<PoolKey, Pool> pools {get; private set;}
        public NativeParallelMultiHashMap<int, int> boundary_BM {get; private set;}

        public bool paramsReady = false;
        public bool ready = false;
        public bool updateWater = false;
        public float magnitude = 0.5f;

        const int PARTICLE_COUNT = 1; // maybe leave some overhead threads for other jobs to run during erosion? Remeshing comes to mind
        const int EVENT_LIMIT = 100000; // event limit per particle before intermediate results are calculated. Should align to a frame or second or something...?
        const int Cycles = 10;

        private Mesh waterMesh;
        private ComputeBuffer poolBuffer;
        private ComputeBuffer streamBuffer;
        private ComputeBuffer heightBuffer;
        private ComputeBuffer argsBuffer;
        private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        // private Matrix4x4[] waterMatrix;
        private Bounds bounds;
        UpdatePoolValues updateJob;
        DrawPoolsJob drawJob;

    #if UNITY_EDITOR
        private enum CHANNEL
        {
            R = 0,
            G = 4,
            B = 8,
            A = 12
        }

        public Texture2D texture;

        void CreateTexture(){
            texture = new Texture2D(generatorResolution, generatorResolution, TextureFormat.RGBAFloat, false);
        }

        void ApplyTexture(){
            Debug.Log($"apply to texture -> {texture.height}, {texture.width}");
            foreach (CHANNEL c in new CHANNEL[] {CHANNEL.G, CHANNEL.B, CHANNEL.R}){ //, CHANNEL.R
                // if (c == inputChannel){continue;};
                NativeSlice<float> CS = new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>((int) c);
                // CS.CopyFrom(new NativeSlice<float>(poolMap));
                // CS.CopyFrom(new NativeSlice<float>(debugViz));
                // CS.CopyFrom(new NativeSlice<float>(particleTrack));
                CS.CopyFrom(new NativeSlice<float>(heightMap));
            }
            texture.Apply();
        }
    #endif
    
        public string getBufferName(string contextAlias){
            string buffer = $"{tileData.pos.x * meshResolution}_{tileData.pos.y * meshResolution}__{generatorResolution}__{contextAlias}";
            Debug.Log(buffer);
            return buffer;
        }

        public void SetFromTileGenerator(TileRequest request, MeshTileGenerator generator){
            double patchRes = (tileResolution * 1.0) / tileSize;
            
            this.tileData = request;
            this.stateManager = generator.pipelineManager;
            this.tileHeight = generator.tileHeight;
            this.tileSize = generator.tileSize;
            this.generatorResolution = generator.generatorResolution;
            this.tileResolution = generator.tileResolution;
            this.meshResolution = generator.meshResolution;
            this.poolMaterial = generator.poolMaterial;
            this.streamMaterial = generator.streamMaterial;
            Debug.Log($"{request.uuid} has a pool drawer");
            paramsReady = true;

        }

        public bool CheckDepends(){
            bool[] notReady = new bool[] {
                !stateManager.BufferExists<NativeArray<float>>(getBufferName("TERRAIN_HEIGHT")),
                stateManager.IsLocked<NativeArray<float>>(getBufferName("TERRAIN_HEIGHT")),
                !stateManager.BufferExists<NativeParallelHashMap<int, int>>(getBufferName("PARTERO_CATCHMENT")),
                stateManager.IsLocked<NativeParallelHashMap<int, int>>(getBufferName("PARTERO_CATCHMENT")),
                !stateManager.BufferExists<NativeParallelHashMap<PoolKey, Pool>>(getBufferName("PARTERO_POOLS")),
                stateManager.IsLocked<NativeParallelHashMap<PoolKey, Pool>>(getBufferName("PARTERO_POOLS")),
                !stateManager.BufferExists<NativeParallelMultiHashMap<int, int>>(getBufferName("PARTERO_BOUNDARY_BM")),
                stateManager.IsLocked<NativeParallelMultiHashMap<int, int>>(getBufferName("PARTERO_BOUNDARY_BM"))
            };
            if(notReady.Contains<bool>(true)){
                Debug.Log($"PoolDrawerNotready! :  {String.Join(", ", notReady)}");
                return false;
            }
            Debug.Log("PoolDrawer Depends ok!");
            return true;
        }

        public void Setup(){
            if(!paramsReady || !CheckDepends()){
                return;
            }
            erosionJobCtl = new StandAloneJobHandler();
            debugViz = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_DEBUG"), generatorResolution * generatorResolution);

            // precalculated buffers
            pools = stateManager.GetBuffer<PoolKey, Pool, NativeParallelHashMap<PoolKey, Pool>>(getBufferName("PARTERO_POOLS"));
            catchment = stateManager.GetBuffer<int, int, NativeParallelHashMap<int, int>>(getBufferName("PARTERO_CATCHMENT"));
            heightMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT"));
            boundary_BM = stateManager.GetBuffer<int, int, NativeParallelMultiHashMap<int, int>>(getBufferName("PARTERO_BOUNDARY_BM"));
            drainToMinima = stateManager.GetBuffer<PoolKey, int, NativeParallelMultiHashMap<PoolKey, int>>(getBufferName("PARTERO_DRAIN_TO_MINIMA"), generatorResolution);

            particles = stateManager.GetBuffer<Particle, NativeArray<Particle>>(getBufferName("PARTERO_PARTICLE_AGENT"), PARTICLE_COUNT, NativeArrayOptions.ClearMemory);
            events = stateManager.GetBuffer<ErosiveEvent, NativeQueue<ErosiveEvent>>(getBufferName("PARTERO_EVT_EROSIVE"), 10);  // queue size is unrestricted, but we limit this in the job
            poolUpdates = stateManager.GetBuffer<PoolUpdate, NativeQueue<PoolUpdate>>(getBufferName("PARTERO_EVT_POOL"), 10);
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

            // waterMatrix = new Matrix4x4[] {
            //     Matrix4x4.TRS( transform.position + new Vector3( 0.5f * tileSize, 0f,  0.5f * tileSize), Quaternion.identity, 400 * Vector3.one )
            // };
            bounds = new Bounds(transform.position, new Vector3(10000, 10000, 10000));
            waterMesh = MeshHelper.SquarePlanarMesh(meshResolution, tileHeight, tileSize);
            for(int i = 0; i < particles.Length; i++){
                Particle p = particles[i];
                p.isDead = true;
                particles[i] = p;
            }
            args[0] = (uint)waterMesh.GetIndexCount(0);
            args[1] = (uint)1;
            argsBuffer.SetData(args);
            #if UNITY_EDITOR
            CreateTexture();
            #endif
            ready = true;
            ApplyTexture();
            Debug.Log("PoolDrawer Ready!");
        }
        
        void Awake(){
            
        }


        public void PushBuffer(){
            streamBuffer.SetData(streamMap);
            poolBuffer.SetData(poolMap);
            heightBuffer.SetData(heightMap);
        }

        public void Update(){
            if(!ready){
                Setup();
                return;
            }else if(erosionJobCtl.JobComplete()){
                erosionJobCtl.CloseJob();
                poolUpdates.Clear();
                Debug.Log("Job done!");
                PushBuffer();
                #if UNITY_EDITOR
                // ApplyTexture();
                #endif
            }else if(updateWater && !erosionJobCtl.isRunning){
                // GenerateJunk();
                // ScheduleJob();
                TriggerCycle();
                // updateWater = false;
            }
            // else if(updateWater){
            //     updateWater = false;
            //     Debug.LogError("job still running??");
            // }
            DrawWater();
        }

        public void RegisterChange(Vector2Int pos, float volume){
            poolUpdates.Enqueue(
                new PoolUpdate {
                    minimaIdx = GetAssociatedMinima(pos),
                    volume = volume
                }
            );
            updateWater = true;
        }

        public int GetAssociatedMinima(Vector2Int pos){
            pos.x += marginRes;
            pos.y += marginRes;
            int idx = (pos.x * generatorResolution) + pos.y;
            if(!catchment.ContainsKey(idx)){
                throw new ArgumentException("No catchment at this location");
            }
            int minimaIdx = 0;
            catchment.TryGetValue(idx, out minimaIdx);
            return minimaIdx;
        }

        public PoolKey GetAssociateFirstOrderKey(int minimaIdx){
            return new PoolKey() { idx = minimaIdx, order = 1, n = 0 };
        }

        public void GetAssociatedPools(PoolKey key, out HashSet<Pool> direct, out HashSet<Pool> peers){
            direct = new HashSet<Pool>();
            peers = new HashSet<Pool>();
            Pool pool = new Pool(){ indexMinima = -1 };
            key = RollUpToCurrent(key);
            while(pools.ContainsKey(key)){
                pools.TryGetValue(key, out pool);
                if(!pool.Exists()) return;
                direct.Add(pool);
                CollectAssociatedPools(pool, ref direct, ref peers);
                if(pool.HasParent()){
                    key = pool.supercededBy;
                }else{
                    return;
                }
            }
        }

        public PoolKey RollUpToCurrent(PoolKey key){
            PoolKey last = new PoolKey();
            Pool pool = new Pool(){ indexMinima = -1 };
            while(pools.ContainsKey(key)){
                pools.TryGetValue(key, out pool);
                if(!pool.Exists()) return last;
                if(pool.HasParent() && pools[pool.supercededBy].volume > 0f){
                    last = key;
                    key = pool.supercededBy;
                }else{
                    return key;
                }
            }
            throw new Exception();
        }

        public void CollectAssociatedPools(Pool pool, ref HashSet<Pool> direct, ref HashSet<Pool> peers){
            Pool peer = new Pool();
            PoolKey key = new PoolKey();
            bool foundNew = false;
            for(int i = 0; i < pool.PeerCount(); i ++){
                key = pool.GetPeer(i);
                pools.TryGetValue(key, out peer);
                if(direct.Contains(peer)) {
                    continue;
                }
                if(peers.Add(peer)){
                    CollectAssociatedPools(peer, ref direct, ref peers);
                }
            }
        }
        
        // public void GenerateJunk(){
            
        //     NativeArray<PoolKey> keys = pools.GetKeyArray(Allocator.Temp);
        //     Pool pool = new Pool();
        //     foreach(PoolKey key in keys){
        //         pools.TryGetValue(key, out pool);
        //         poolUpdates.Add(
        //             new PoolUpdate {
        //                 minimaIdx = key.idx,
        //                 volume = 0.5f * magnitude * pool.capacity
        //             }
        //         );
        //     }
        //     keys.Dispose();
        // }

        public void TriggerCycle(){
            JobHandle handle = default;
            for (int i = 0; i < Cycles; i++){
                // JobHandle cycleJob = ErosionCycleJob.Schedule(heightMap, poolMap, streamMap, particleTrack, particles, events, EVENT_LIMIT, generatorResolution, handle);
                // JobHandle processJob = ProcessErosiveEventsJob.ScheduleRun(heightMap, poolMap, streamMap, particleTrack, events, poolUpdates, catchment, generatorResolution, cycleJob);
                // handle = processJob;
                handle = ErosionCycleJob.Schedule(heightMap, poolMap, streamMap, particleTrack, particles, events, EVENT_LIMIT, generatorResolution, handle);
            }
            // JobHandle writerJob = WriteErosionMaps.ScheduleRun(heightMap, poolMap, streamMap, particleTrack, events, poolUpdates, catchment, boundary_BM, pools, generatorResolution, handle);
            Debug.LogWarning("Cycle started");
            // erosionJobCtl.TrackJob(writerJob);
            erosionJobCtl.TrackJob(handle);
        }

        // public void ScheduleJob(){
        //     JobHandle first = UpdatePoolValues.ScheduleRun(
        //         poolUpdates,
        //         pools,
        //         default
        //     );
        //     // JobHandle second = PoolInterpolationDebugJob.ScheduleJob(pools, first);
        //     JobHandle third = DrawPoolsJob.Schedule(
        //         new NativeSlice<float>(poolMap),
        //         new NativeSlice<float>(heightMap),
        //         catchment,
        //         boundary_BM,
        //         pools,
        //         generatorResolution,
        //         // second
        //         first
        //     );
        //     erosionJobCtl.TrackJob(third);
        // }

        // public void ScheduleJob(){
        //     JobHandle first = UpdatePoolValues.ScheduleRun(
        //         poolUpdates,
        //         pools,
        //         default
        //     );
        //     // JobHandle second = PoolInterpolationDebugJob.ScheduleJob(pools, first);
        //     JobHandle third = DrawPoolsJob.Schedule(
        //         new NativeSlice<float>(poolMap),
        //         new NativeSlice<float>(heightMap),
        //         catchment,
        //         boundary_BM,
        //         pools,
        //         generatorResolution,
        //         // second
        //         first
        //     );
        //     erosionJobCtl.TrackJob(third);
        // }

        public void DrawWater(){
            Graphics.DrawMeshInstancedIndirect(waterMesh, 0, poolMaterial, bounds, argsBuffer, 0, poolMatProps);
            Graphics.DrawMeshInstancedIndirect(waterMesh, 0, streamMaterial, bounds, argsBuffer, 0, streamMatProps);
        }

        public void OnDestroy(){
            argsBuffer.Release();
            poolBuffer.Release();
            heightBuffer.Release();
            streamBuffer.Release();
        }


    }
}
