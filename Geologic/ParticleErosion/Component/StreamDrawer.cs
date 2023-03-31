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
using xshazwar.noize.mesh;
using xshazwar.noize.mesh.Generators;
using xshazwar.noize.mesh.Streams;
using xshazwar.noize.scripts;
using xshazwar.noize.tile;

namespace xshazwar.noize.geologic {
    
    [AddComponentMenu("Noize/StreamDrawer", 0)]
    public class StreamDrawer : MonoBehaviour {
        
        private int meshResolution = 512;
        private bool isSetup = false;

        CustomRenderTexture buffer0;
        CustomRenderTexture buffer1;

        MeshRenderer mRenderer;
        public Material referenceMat;
        Material updateMat;
        IProvideGeodata geoProvider;

        public bool updateMaterial = false;

        void Awake(){
            geoProvider = GetComponent<IProvideGeodata>();
            geoProvider.OnGeodataReady += ErosionReady;
            this.enabled = false;
        }

        void Update(){
            if(updateMaterial){
                updateMaterial = false;
                UpdateMaterial();
            }
        }

        void ErosionReady(){
            this.enabled = true;
        }

        public void SetParams(TileSetMeta tileMeta, Material mat){
            meshResolution = tileMeta.TILE_RES.x;
            referenceMat = mat;
        }

        void OnEnable(){
            mRenderer = GetComponent<MeshRenderer>();
            InitBuffers();
            SetupMaterial();
            // buffer0.Update();
            // buffer1.Update();
            mRenderer.material = updateMat;
            geoProvider.OnWaterUpdate += UpdateBuffers;
            isSetup = true;
        }

        public void SetupMaterial(){
            if(referenceMat != null){
                updateMat = new Material(referenceMat);
                UpdateMaterial();
            }
        }

        public void UpdateMaterial(){
            updateMat.CopyPropertiesFromMaterial(referenceMat);
            updateMat.SetTexture("_StreamControl", buffer0);
            updateMat.SetTexture("_CavityMap", buffer1);
        }

        public void InitBuffers(){
            buffer0 = new CustomRenderTexture(meshResolution, meshResolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            buffer0.initializationMode = CustomRenderTextureUpdateMode.OnDemand;
            buffer0.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            buffer0.initializationSource = CustomRenderTextureInitializationSource.TextureAndColor;
            buffer0.initializationTexture = geoProvider.GetWaterControlTexture();
            buffer0.depth = 0;
            // buffer0.material = updateMat;
            buffer0.Create();
            buffer0.Initialize();

            buffer1 = new CustomRenderTexture(meshResolution, meshResolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            buffer1.initializationMode = CustomRenderTextureUpdateMode.OnDemand;
            buffer1.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            buffer1.initializationSource = CustomRenderTextureInitializationSource.TextureAndColor;
            buffer1.initializationTexture = geoProvider.GetTerrainControlTexture();
            buffer1.depth = 0;
            buffer1.Create();
            buffer1.Initialize();
        }

        public void UpdateBuffers(){
            Graphics.CopyTexture(geoProvider.GetWaterControlTexture() , buffer0);
            Graphics.CopyTexture(geoProvider.GetTerrainControlTexture() , buffer1);
        }


        void OnDisable()
         {
            if (!isSetup) return;
            geoProvider.OnWaterUpdate -= UpdateBuffers;
            buffer0.Release();
            DestroyImmediate(buffer0);
            buffer1.Release();
            DestroyImmediate(buffer1);
        }

        void OnDestroy(){
            geoProvider.OnGeodataReady -= ErosionReady;
        }


    }
}