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
using xshazwar.noize.mesh;

namespace xshazwar.noize.scripts {
    
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MeshGenerator : BasePipeline{
        
        public TileGenerator source;
        public MeshStageData input;

        public MeshFilter meshFilter;

        public Action<StageIO> onResult;

        private bool RunMe;
        private bool complete;
        protected override void BeforeStart()
        {
            input.mesh = new Mesh();
			gameObject.GetComponent<MeshFilter>().mesh = input.mesh;

            RunMe = false;
            complete = false;
            source.onResult += CreateMeshFromData;
            onResult += SetResult;
        }

        public void CreateMeshFromData(StageIO p){
            GeneratorData d = (GeneratorData) p;
            input.resolution = d.resolution;
            input.data = d.data;
            RunMe = true;

        }

        protected override void BeforeUpdate(){
            if (RunMe){
                Schedule(input, onResult);
                RunMe = false;
            } if (complete){
                complete = false;
            }
        }

        public void SetResult(StageIO d){
            
            complete = true;
            Debug.Log("Result Set!");
        }
    }
}