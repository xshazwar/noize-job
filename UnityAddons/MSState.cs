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

// using static Unity.Mathematics.math;
// using Unity.Mathematics;

// using xshazwar.noize.pipeline;
// using xshazwar.noize;
// using xshazwar.noize.filter.blur;
// using xshazwar.noize.scripts;
// using xshazwar.noize.mesh;
// using xshazwar.noize.mesh.Generators;
// using xshazwar.noize.mesh.Streams;
// using xshazwar.noize.geologic;

using JBooth.MicroSplat;

namespace xshazwar.noize {
    
    [AddComponentMenu("Noize/MicrosplatState", 0)]
    public class MSState : MonoBehaviour {

        MicroSplatObject msObject;
        public MeshRenderer rend;
        public Material mat;
        public Shader shader;

        void Awake(){
            this.enabled = false;
        }

        void OnEnable(){
            if(msObject == null){
                msObject = GetComponent<MicroSplatObject>();
            }
            if(msObject != null && mat == null){
                mat = msObject.matInstance;
            }
            if(rend != null && mat == null){
                mat = rend.material;
            }
            if (mat != null && shader == null){
                shader = mat.shader;
            }
            if (shader == null){
                return;
            }
            int c = shader.GetPropertyCount();
            for( int i = 0; i < c; i++){
                HandleProperty(i);
            }
        }

        void OnDisable(){

        }

        public void HandleProperty(int idx){
            
            ShaderPropertyType t = shader.GetPropertyType(idx);
            string name = shader.GetPropertyName(idx);

            switch(t){
                case ShaderPropertyType.Color:
                    Color c = mat.GetColor(name);
                    Debug.Log($"{t}:: {name}: {c}");
                    break;
                case ShaderPropertyType.Vector:
                    Vector4 v = mat.GetVector(name);
                    Debug.Log($"{t}:: {name}: {v}");
                    break;
                case ShaderPropertyType.Float:
                    float f = mat.GetFloat(name);
                    Debug.Log($"{t}:: {name}: {f}");
                    break;
                case ShaderPropertyType.Range:
                    float r = mat.GetFloat(name);
                    Debug.Log($"{t}:: {name}: {r}");
                    break;
                case ShaderPropertyType.Texture:
                    Debug.Log($"{t}:: {name}");
                    break;
                case ShaderPropertyType.Int:
                    int i = mat.GetInteger(name);
                    Debug.Log($"{t}:: {name}: {i}");
                    break;
            }
        }
    }
}