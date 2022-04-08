using System;

using UnityEngine;
using UnityEditor;

using Unity.Collections;
using static Unity.Mathematics.math;
using Unity.Mathematics;

using xshazwar.noize.pipeline;
using xshazwar.noize.scripts;


namespace xshazwar.noize.scripts.editor {

    [Serializable]
    public enum CHANNEL
    {
        R = 0,
        G = 4,
        B = 8,
        A = 12
    }
    public class VisualizePipelineWindow : EditorWindow
    {
        BasePipeline mPipeline;
        Texture2D texture;

        public int resolution = 256;
        public int xpos = 0;
        public int zpos = 0;

        public bool isRunning = false;
        private bool useInputTexture = false;
        private CHANNEL inputChannel = 0;

        private Texture2D inputTexture;

        private Action<StageIO> pipelineComplete;

        [MenuItem("Noize/Visualize Pipeline")]
        static void ShowWindow() {
            VisualizePipelineWindow window = CreateInstance<VisualizePipelineWindow>();
            GUIContent title = new GUIContent("VisualizePipelineWindow");

            window.titleContent = title;
            window.position = new Rect(0, 0, 800, 700);
            window.Show();
        }

        public void OnUpdate(){
            if (! isRunning || mPipeline == null){
                return;
            }
            try{
                foreach(BasePipeline p in mPipeline.GetDependencies()){
                    if(p != null){
                        Debug.Log($"Upating {p.alias}");
                        p.Update();
                    }
                }
            }catch(NullReferenceException){
                Debug.LogError("Could not update pipelines");
                isRunning = false;
            }
                
        }

        void OnEnable(){
            EditorApplication.update += OnUpdate;
            pipelineComplete += OnPipelineComplete;
        }

        void OnDisable(){
            EditorApplication.update -= OnUpdate;
            pipelineComplete -= OnPipelineComplete;
            foreach(BasePipeline p in mPipeline.GetDependencies()){
                if(p != null){
                    Debug.Log($"destroying {p.alias}");
                    p.OnDestroy();
                }
            }
        }

        void ApplyTexture(NativeSlice<float> cd){
            foreach (CHANNEL c in new CHANNEL[] {CHANNEL.R, CHANNEL.G, CHANNEL.B}){
                if (c == inputChannel){continue;};
                NativeSlice<float> CS = new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>((int) c);
                CS.CopyFrom(cd);
            }
            texture.Apply();
        }

        void ImportTexture(){
                CreateTexture();
                RenderTexture renderTex = RenderTexture.GetTemporary(
                    inputTexture.width,
                    inputTexture.height,
                    0,
                    RenderTextureFormat.Default,
                    RenderTextureReadWrite.Linear);
                Graphics.Blit(inputTexture, renderTex);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTex;
                texture.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTex);
                NativeSlice<float> channelData = new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>((int) inputChannel);
                ApplyTexture(channelData);
        }

        void OnPipelineComplete(StageIO res){
            Debug.Log("Pipeline complete for visualizer, apply image -> visualizer");
            GeneratorData d = (GeneratorData) res;
            isRunning = false;
            ApplyTexture(d.data);
            Debug.Log("Image Applied.");
        }

        void CreateTexture(){
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false);
        }

        public void RunPipeline(){
            try{
                Debug.Log("new pipeline run");
                isRunning = true;
                if (texture == null){
                    CreateTexture();
                }
                mPipeline.Enqueue(
                    new GeneratorData {
                        uuid = "gui job",
                        resolution = resolution,
                        xpos = resolution *  xpos,
                        zpos = resolution *  zpos,
                        data = new NativeSlice<float4>(texture.GetRawTextureData<float4>()).SliceWithStride<float>((int)inputChannel)
                    },
                    pipelineComplete
                );
            }catch(NullReferenceException){
                Debug.LogError("Could not run pipeline");
                isRunning = false;
            }
        }

        void OnGUI()
        {
            int offset = 0;
            GUILayout.Label("Pipeline", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
                resolution = EditorGUILayout.IntField("Resolution:", resolution);
            if (EditorGUI.EndChangeCheck())
            {
                CreateTexture();
            }
            xpos = EditorGUILayout.IntField("X Tile Position:", xpos);
            zpos = EditorGUILayout.IntField("Z Tile Position:", zpos);
            useInputTexture = EditorGUILayout.Toggle("Pipe takes input texture", useInputTexture);
            if(useInputTexture){
                offset += 20;
                EditorGUI.BeginChangeCheck();
                inputChannel = (CHANNEL)EditorGUILayout.EnumPopup("Input Channel", inputChannel);
                if (EditorGUI.EndChangeCheck())
                {
                    if (inputTexture != null){
                        ImportTexture();
                    }
                }


            }
            EditorGUI.BeginChangeCheck();
                mPipeline = (BasePipeline)EditorGUI.ObjectField(new Rect(3, 100 + offset, 400, 20),
                    "Select Pipeline:",
                    mPipeline,
                    typeof(BasePipeline),
                    true);
            if (EditorGUI.EndChangeCheck())
            {
                if( mPipeline != null){
                    Debug.Log($"Setting up {mPipeline.alias}");
                    foreach(BasePipeline pl in mPipeline.GetDependencies()){
                        if (pl != null && pl.stages.Count > 0){
                            pl.Start();
                        }else{
                            Debug.LogError("No stages in pipeline");
                        }
                    }
                }
                
            }
            EditorGUI.BeginChangeCheck();
                if(useInputTexture){
                    offset += 140;
                    inputTexture = (Texture2D)EditorGUI.ObjectField(new Rect(3, 3 + offset, 200, 30),
                    "Add a Texture:",
                    inputTexture,
                    typeof(Texture2D));
                }
            if (EditorGUI.EndChangeCheck()){
                resolution = inputTexture.width;
                ImportTexture();
            }
            if (useInputTexture && inputTexture){
                EditorGUI.DrawPreviewTexture(new Rect(240, 3 + offset, 256, 256), inputTexture);
                offset += 256;
            }

            if (texture)
            {
                EditorGUI.PrefixLabel(new Rect(150, 140 + offset, 50, 15), 0, new GUIContent("Preview:"));
                EditorGUI.DrawPreviewTexture(new Rect(240, 140 + offset, 512, 512), texture);
            }

            if (GUI.Button(new Rect(10, 140 + offset, 100, 30), "Run Pipeline")){
                
                if (isRunning == false && mPipeline != null){
                    foreach(BasePipeline pl in mPipeline.GetDependencies()){
                        if (pl != null && pl.stages.Count > 0){
                            Debug.Log($"restarting {pl.alias}");
                            pl.OnDestroy();
                            pl.Start();
                        }else{
                            Debug.LogError("No stages in pipeline");
                        }
                    }
                    RunPipeline();
                }else{
                    Debug.Log($"running {isRunning} | pipeline not null ? {mPipeline != null}");
                }

                // if (isRunning == false && mPipeline != null){
                //     mPipeline.OnDestroy();
                //     mPipeline.Start();
                //     RunPipeline();
                // }
            }
            if( inputTexture != null){
                if (GUI.Button(new Rect(10, 100 + offset, 100, 30), "Reset Source")){
                    if (isRunning == false && mPipeline != null){
                        ImportTexture();
                    }
                }
            }
        }
    }
}