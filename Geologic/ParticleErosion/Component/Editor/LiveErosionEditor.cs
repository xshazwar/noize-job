using UnityEngine;
using UnityEditor;

using xshazwar.noize;
using xshazwar.noize.geologic;

namespace xshazwar.noize.editor {
    [CustomEditor(typeof(LiveErosion))]
    public class LiveErosionEditor : Editor
    {
        SerializedProperty tex2d;
        SerializedProperty meshType;
        SerializedProperty updateContinuous;
        SerializedProperty updateSingle;
        SerializedProperty resetLand;
        SerializedProperty resetWater;
        SerializedProperty showMap;
        SerializedProperty performErosion;
        SerializedProperty erosionSettings;
        SerializedProperty updateTexture;
        SerializedProperty drawPools;
        SerializedProperty poolColor;
        SerializedProperty debugDescent;
        Texture2D texture;
        LiveErosion? erosionctl = null;

        void OnEnable(){
            if (erosionctl == null){
                erosionctl = (LiveErosion) target;
                erosionctl.EnabledEvent += Connect;
                erosionctl.DisabledEvent += Disconnect;
            }
            Init();
        }

        void OnDisable(){
            erosionctl.EnabledEvent -= Connect;
            erosionctl.DisabledEvent -= Disconnect;
            erosionctl = null;
            tex2d = null;
        }

        void Init(){
            tex2d = serializedObject.FindProperty("texture");
            meshType = serializedObject.FindProperty("meshType");
            updateContinuous = serializedObject.FindProperty("updateContinuous");
            updateSingle = serializedObject.FindProperty("updateSingle");
            resetLand = serializedObject.FindProperty("resetLand");
            resetWater = serializedObject.FindProperty("resetWater");
            showMap = serializedObject.FindProperty("showMap");
            performErosion = serializedObject.FindProperty("performErosion");
            erosionSettings = serializedObject.FindProperty("erosionSettings");
            updateTexture = serializedObject.FindProperty("updateTexture");
            drawPools = serializedObject.FindProperty("drawPools");
            poolColor = serializedObject.FindProperty("byteColor");
            debugDescent = serializedObject.FindProperty("debugDescent");
            Connect();
            
        }

        void Connect(){
            tex2d = serializedObject.FindProperty("texture");
            texture = null;
            texture = tex2d.objectReferenceValue as Texture2D;
        }
        void Disconnect(){
            tex2d = null;
            texture = Texture2D.blackTexture;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            // Rect lastRect = GUILayoutUtility.GetLastRect();
            // Rect buttonRect = new Rect(lastRect.x, lastRect.y + EditorGUIUtility.singleLineHeight, 100, 30);
            
            if(GUILayout.Button("AddNewItem", GUILayout.Width(100), GUILayout.Height(30))){
                Debug.Log("Here we go!");
                erosionctl.SaveErosionState();
                Debug.Log("Chili Dog!");
            }
            
            EditorGUILayout.PropertyField(meshType);
            EditorGUILayout.PropertyField(debugDescent);
            EditorGUILayout.PropertyField(resetLand);
            EditorGUILayout.PropertyField(resetWater);

            EditorGUILayout.PropertyField(drawPools);
            // this is cheesy but it works
            // sticking the texture into a label didn't
            EditorGUILayout.PropertyField(updateTexture);
            
            EditorGUILayout.PropertyField(performErosion);
            EditorGUILayout.PropertyField(updateSingle);
            EditorGUILayout.PropertyField(updateContinuous);
            EditorGUILayout.PropertyField(poolColor);
            EditorGUILayout.PropertyField(erosionSettings);
            if((bool)updateTexture.boolValue){
                EditorGUILayout.PropertyField(showMap);
                Rect space = EditorGUILayout.BeginHorizontal();
                // GUILayout.Box(texture, GUILayout.Width (512), GUILayout.Height (512));
                EditorGUI.DrawPreviewTexture(space, texture);
                EditorGUILayout.TextArea("", GUIStyle.none, GUILayout.Height(1024));
                EditorGUILayout.EndHorizontal();
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}