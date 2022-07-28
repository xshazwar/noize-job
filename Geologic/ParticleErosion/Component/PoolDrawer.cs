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
        private int tileHeight = 1000;
        private int tileSize = 1000;
        private int generatorResolution = 1000;
        private int tileResolution = 1000;
        private int margin = 5;
        private Material waterMaterial;
        public StandAloneJobHandler jobctl;
        public PipelineStateManager stateManager;
        private NativeSlice<float> poolMap;
        private NativeSlice<float> heightMap;
        private NativeParallelHashMap<int, int> catchment;
        private NativeParallelHashMap<PoolKey, Pool> pools;
        public NativeList<PoolUpdate> poolUpdates;
        public bool ready = false;
        public bool updateWater = false;
        public float magnitude = 0.5f;
        UpdatePoolValues updateJob;
        DrawPoolsJob drawJob;

    #if UNITY_EDITOR
        public Texture2D texture;

        void CreateTexture(){
            texture = new Texture2D(generatorResolution, generatorResolution, TextureFormat.RGBAFloat, false);
        }

        void ApplyTexture(){
            NativeSlice<float> CS = new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>((int) 4);
            CS.CopyFrom(poolMap);
            texture.Apply();
        }
    #endif
    
        private string getBufferName(string contextAlias){
            return $"{tileData.pos.x}_{tileData.pos.y}__{generatorResolution}__{contextAlias}";
        }

        public void SetFromTileGenerator(TileRequest request, MeshTileGenerator generator){
            this.tileData = request;
            this.stateManager = generator.pipelineManager;
            this.tileHeight = generator.tileHeight;
            this.tileSize = generator.tileSize;
            this.generatorResolution = generator.generatorResolution;
            this.tileResolution = generator.tileResolution;
            this.margin = generator.margin;
            // this.waterMaterial = 
            Debug.Log($"{request.uuid} has a pool drawer");
        }

        public bool CheckDepends(){
            bool[] notReady = new bool[] {
                !stateManager.BufferExists<NativeArray<float>>(getBufferName("TERRAIN_HEIGHT")),
                stateManager.IsLocked<NativeArray<float>>(getBufferName("TERRAIN_HEIGHT")),
                !stateManager.BufferExists<NativeParallelHashMap<int, int>>(getBufferName("PARTERO_CATCHMENT")),
                stateManager.IsLocked<NativeParallelHashMap<int, int>>(getBufferName("PARTERO_CATCHMENT")),
                !stateManager.BufferExists<NativeParallelHashMap<PoolKey, Pool>>(getBufferName("PARTERO_POOLS")),
                stateManager.IsLocked<NativeParallelHashMap<PoolKey, Pool>>(getBufferName("PARTERO_POOLS"))
            };
            if(notReady.Contains<bool>(true)){
                Debug.Log("PoolDrawerNotready!");
                // foreach(bool b in notReady){
                //     Debug.Log(b);
                // }
                return false;
            }
            return true;
        }

        public void Setup(){
            if(!CheckDepends()){
                return;
            }
            jobctl = new StandAloneJobHandler();
            pools = stateManager.GetBuffer<PoolKey, Pool, NativeParallelHashMap<PoolKey, Pool>>(getBufferName("PARTERO_POOLS"), generatorResolution * generatorResolution);
            poolUpdates = stateManager.GetBuffer<PoolUpdate, NativeList<PoolUpdate>>(getBufferName("PARTERO_FAKE_POOLUPDATE"), 2 * pools.Count());
            catchment = stateManager.GetBuffer<int, int, NativeParallelHashMap<int, int>>(getBufferName("PARTERO_CATCHMENT"), generatorResolution * generatorResolution);
            poolMap = new NativeSlice<float>(
                stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_WATERMAP_POOL"), generatorResolution * generatorResolution)
            );
            heightMap = new NativeSlice<float>(
                stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT"), generatorResolution * generatorResolution)
            );
            #if UNITY_EDITOR
            CreateTexture();
            #endif
            ready = true;
        }
        
        void Awake(){
            
        }

        public void Update(){
            if(!ready){
                Setup();
            }else if(jobctl.JobComplete()){
                jobctl.CloseJob();
                Debug.Log("Job done!");
                #if UNITY_EDITOR
                ApplyTexture();
                #endif
            }else if(updateWater){
                GenerateJunk();
                ScheduleJob();
                updateWater = false;
            }
        }

        public void GenerateJunk(){
            poolUpdates.Clear();
            NativeArray<PoolKey> keys = pools.GetKeyArray(Allocator.Temp);
            Pool pool = new Pool();
            foreach(PoolKey key in keys){
                pools.TryGetValue(key, out pool);
                poolUpdates.Add(
                    new PoolUpdate {
                        minimaIdx = key.idx,
                        volume = 0.5f * magnitude * pool.capacity
                    }
                );
            }
            keys.Dispose();
        }

        public void ScheduleJob(){
            JobHandle first = UpdatePoolValues.ScheduleRun(
                poolUpdates,
                pools,
                default
            );
            JobHandle second = DrawPoolsJob.Schedule(
                poolMap,
                heightMap,
                catchment,
                pools,
                generatorResolution,
                first
            );
            jobctl.TrackJob(second);
        }


    }
}
