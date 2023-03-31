using System;
// using System.Collections;
// using System.Collections.Generic;

using UnityEngine;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.mesh;
using xshazwar.noize.mesh.Generators;
using xshazwar.noize.mesh.Streams;
using xshazwar.noize.pipeline;
using xshazwar.noize.scripts;
using xshazwar.noize.tile;

namespace xshazwar.noize.geologic {
    [AddComponentMenu("Noize/TileDrawer", 0)]
    public class TileDrawer : MonoBehaviour, IProvideGeodata {

        private TileRequest tileData;
        private TileSetMeta tileMeta;

        // public int tileMeta.HEIGHT {get; private set;}
        // public int tileMeta.TILE_SIZE.x {get; private set;}
        // public int tileMeta.GENERATOR_RES.x {get; private set;}
        // public int tileMeta.TILE_RES.x {get; private set;}
        // public int tileMeta.MARGIN {get; private set;}

        public PipelineStateManager stateManager;

        public NativeArray<float> heightMap {get; private set;}
        public NativeArray<float> poolMap {get; private set;}
        public NativeArray<float> streamMap {get; private set;}
        private ComputeBuffer poolBuffer;
        private ComputeBuffer heightBuffer;
        private ComputeBuffer argsBuffer;

        // Water Control Texture
        public Texture2D waterControl;
        public Texture2D textureControl;

        public Texture2D GetWaterControlTexture(){ return waterControl; }
        public Texture2D GetTerrainControlTexture(){ return textureControl; }

        // Meshing
        private MeshBakery bakery;
        private Mesh landMesh;
        private Mesh.MeshDataArray meshDataArray;
        private Mesh.MeshData meshData;
        private Mesh waterMesh;

         // Instanced Drawing
        private Material poolMaterial;
        private MaterialPropertyBlock poolMatProps;
        private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        private Bounds bounds;

        public Action OnGeodataReady {get; set;}
        public Action OnWaterUpdate {get; set;}

        public string getBufferName(string contextAlias){
            string buffer = $"{tileData.pos.x * tileMeta.TILE_RES.x}_{tileData.pos.y * tileMeta.TILE_RES.x}__{tileMeta.GENERATOR_RES.x}__{contextAlias}";
            Debug.Log(buffer);
            return buffer;
        }

        public void LoadMaps(){
            heightMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("TERRAIN_HEIGHT"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x);
            streamMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_WATERMAP_STREAM"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x);
            poolMap = stateManager.GetBuffer<float, NativeArray<float>>(getBufferName("PARTERO_WATERMAP_POOL"), tileMeta.GENERATOR_RES.x * tileMeta.GENERATOR_RES.x); 
            poolBuffer = new ComputeBuffer(heightMap.Length, 4); // sizeof(float)
            heightBuffer = new ComputeBuffer(heightMap.Length, 4); // sizeof(float)
            argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

            poolMatProps = new MaterialPropertyBlock();
            poolMatProps.SetBuffer("_WaterValues", poolBuffer);
            poolMatProps.SetBuffer("_TerrainValues", heightBuffer);
            poolMatProps.SetFloat("_Height", tileMeta.HEIGHT);
            poolMatProps.SetFloat("_Mesh_Size", tileMeta.TILE_SIZE.x);
            poolMatProps.SetFloat("_Mesh_Res", tileMeta.TILE_RES.x * 1.0f);
            poolMatProps.SetFloat("_Data_Res", tileMeta.GENERATOR_RES.x * 1.0f);
            bounds = new Bounds(transform.position, new Vector3(10000, 10000, 10000));
            waterMesh = MeshHelper.SquarePlanarMesh(tileMeta.TILE_RES.x, tileMeta.HEIGHT, tileMeta.TILE_SIZE.x);
            args[0] = (uint)waterMesh.GetIndexCount(0);
            args[1] = (uint)1;
            poolBuffer.SetData(poolMap);
            heightBuffer.SetData(heightMap);
            argsBuffer.SetData(args);
            OnGeodataReady?.Invoke();
        }

        public JobHandle CopyTextures(){
            // TODO profile. Might just bake these textures instead of generating them on the fly
            
            // insert data into textures for MS shader

            JobHandle handle = SetRGBA32Job.ScheduleRun(poolMap, waterControl, ColorChannelByte.R, (JobHandle) default, 1000f);
            // puddle
            handle = SetRGBA32Job.ScheduleRun(poolMap, waterControl, ColorChannelByte.G, handle, 1000f);
            // stream
            handle = SetRGBA32Job.ScheduleRun(streamMap, waterControl, ColorChannelByte.B, handle, 2f);
            
            // WIP
            // handle = CurvitureMapJob.ScheduleRun(textureControl, heightMap, erosionParams, ColorChannelByte.G, tileMeta.GENERATOR_RES.x, handle);
            // erosion
            handle = SetRGBA32Job.ScheduleRun(streamMap, textureControl, ColorChannelByte.A, handle, 1f);
            
            return handle;
        }

        void CreateTexture(){
            waterControl = new Texture2D(tileMeta.TILE_RES.x, tileMeta.TILE_RES.x, TextureFormat.RGBA32, false);
            textureControl = new Texture2D(tileMeta.TILE_RES.x, tileMeta.TILE_RES.x, TextureFormat.RGBA32, false);
        }

        public JobHandle ScheduleMeshUpdate(JobHandle dep){
            meshDataArray = Mesh.AllocateWritableMeshData(1);
			meshData = meshDataArray[0];
			return HeightMapMeshJob<OvershootSquareGridHeightMap, PositionStream32>.ScheduleParallel(
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

        public void OnDestroy(){
            argsBuffer.Release();
            poolBuffer.Release();
            heightBuffer.Release();
        }
    }
}