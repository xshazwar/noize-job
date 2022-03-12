using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

using static Unity.Mathematics.math;
using Unity.Mathematics;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize;
using xshazwar.noize.generate;
using xshazwar.noize.pipeline;
using xshazwar.noize.scripts;

namespace xshazwar.noize.scripts {
    
    [RequireComponent(typeof(Texture2D))]
    public class TileGenerator : MonoBehaviour {
        Texture2D texture;
        NativeSlice<float> data;
        NativeSlice<float> red;
        NativeSlice<float> green;

        public GeneratorPipeline pipeline;
        public Renderer mRenderer;

        public GeneratorData input;

        public Action<StageIO> onResult;

        public bool RunMe;
        private bool complete;
        void Start()
        {
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

        void Update(){
            if (RunMe){
                pipeline.Enqueue(input, onResult);
                RunMe = false;
            }
        }

        public void SetResult(StageIO d){
            red.CopyFrom(data);
            green.CopyFrom(data);
            texture.Apply();
            UnityEngine.Profiling.Profiler.EndSample();
            complete = false;
        }
    }
}