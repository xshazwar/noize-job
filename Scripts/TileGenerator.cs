using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using static Unity.Mathematics.math;
using Unity.Mathematics;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;
using xshazwar.noize.cpu.mutate;
using xshazwar.noize.scripts;

namespace xshazwar.noize.scripts {
    
    [RequireComponent(typeof(Texture2D))]
    public class TileGenerator : BasePipeline{
        Texture2D texture;
        NativeSlice<float> data;
        NativeSlice<float> red;
        NativeSlice<float> green;
        public Renderer mRenderer;
        public int resolution;
        public int xpos;
        public int zpos;

        private GeneratorData input;

        private Action<StageIO> onResult;

        public bool RunMe;
        private bool complete;
        public override void BeforeStart()
        {
            RunMe = false;
            complete = false;
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false);
            mRenderer.material.mainTexture = texture;
            data =  new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>(8);
            red =  new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>(0);
            green =  new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>(4);
            input = new GeneratorData(resolution, xpos, zpos, data);
            onResult += SetResult;
        }
        public override void AfterStart(){}

        public override void BeforeUpdate(){
            if (RunMe){
                input.xpos = xpos;
                input.zpos = zpos;
                input.resolution = resolution;
                Schedule(input, onResult);
                RunMe = false;
            } if (complete){
                texture.Apply();
                complete = false;
            }
        }
        public override void OnPipelineComplete(){}
        public override void AfterUpdate(){}

        public void SetResult(StageIO d){
            GeneratorData dd = (GeneratorData) d;
            complete = true;
            Debug.Log("Result Set!");
        }
    }
}