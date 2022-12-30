using UnityEngine;
using UnityEditor;

using xshazwar.noize;
using xshazwar.noize.geologic;

namespace xshazwar.noize.editor {
    [CustomEditor(typeof(LiveErosion))]
    public class LiveErosionEditor : Editor
    {
        SerializedProperty tex2d;
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
        Texture2D texture;

        void OnEnable(){
            // tex2d = serializedObject.FindProperty("texture");
            // tex2d = serializedObject.FindProperty("waterControl");
            tex2d = serializedObject.FindProperty("textureControl");
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
            
            texture = tex2d.objectReferenceValue as Texture2D;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(resetLand);
            EditorGUILayout.PropertyField(resetWater);

            EditorGUILayout.PropertyField(drawPools);
            EditorGUILayout.PropertyField(erosionSettings);
            // this is cheesy but it works
            // sticking the texture into a label didn't
            EditorGUILayout.PropertyField(updateTexture);
            
            EditorGUILayout.PropertyField(performErosion);
            EditorGUILayout.PropertyField(updateSingle);
            EditorGUILayout.PropertyField(updateContinuous);
            EditorGUILayout.PropertyField(poolColor);
            
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