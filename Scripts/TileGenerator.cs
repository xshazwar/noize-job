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

        public GeneratorData input;

        private Action<StageIO> onResult;

        public bool RunMe;
        private bool complete;
        protected override void BeforeStart()
        {
            if (input.resolution == null){
                throw new Exception("Set a resolution!");
            }
            RunMe = false;
            complete = false;
            texture = new Texture2D(input.resolution, input.resolution, TextureFormat.RGBAFloat, false);
            mRenderer.material.mainTexture = texture;
            data =  new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>(8);
            red =  new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>(0);
            green =  new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>(4);
            input.data = data;
            onResult += SetResult;
        }

        protected override void BeforeUpdate(){
            if (RunMe){
                Setup();
                Schedule(input, onResult);
                RunMe = false;
            } if (complete){
                UnityEngine.Profiling.Profiler.BeginSample("Flush to image");
                red.CopyFrom(data);
                green.CopyFrom(data);
                texture.Apply();
                UnityEngine.Profiling.Profiler.EndSample();
                complete = false;
            }
        }

        public void SetResult(StageIO d){
            GeneratorData dd = (GeneratorData) d;
            complete = true;
            Debug.Log("Result Set!");
        }
    }
}