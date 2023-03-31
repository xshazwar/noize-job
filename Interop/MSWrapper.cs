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

using xshazwar.noize.scripts;

using JBooth.MicroSplat;

namespace xshazwar.noize.interop {
    
    [AddComponentMenu("Noize/MicroSplatWrapper", 0)]
    public class MSWrapper : MicroSplatObject {
        
        MeshTileGenerator tileGen;
        public bool updateShader = true;

        void Awake(){
            tileGen = GetComponent<MeshTileGenerator>();
            
        }

        void Update(){
            if(updateShader){
                // UpdateProcSettings();
                UpdateShaderParams();
            }
        }

        void UpdateProcSettings(){
            #if __MICROSPLAT_PROCTEX__
                procTexCfg = MicroSplatProceduralTexture.FindOrCreateProceduralConfig(tileGen.meshMaterial);
            #endif
        }

        void OnEnable(){
            UpdateShaderParams();
        }

        public void UpdateShaderParams(){
            if (keywordSO == null)
            {
                keywordSO = MicroSplatUtilities.FindOrCreateKeywords(tileGen.meshMaterial);
                // MicroSplatObject.SyncAll();
            }
            propData = MicroSplatShaderGUI.FindOrCreatePropTex(tileGen.meshMaterial);
            ApplySharedData(tileGen.meshMaterial);
            // MicroSplatObject.SyncAll();
            UpdateProcSettings();
            if(procTexCfg == null){
                Debug.LogWarning("No Proctex found for Material");
                return;
            }
            MicroSplatObject.SyncAll();
            // Debug.LogWarning("Material settings applied");
        }

        void OnDisable(){
            keywordSO = null;
            procTexCfg = null;
        }
    }
}