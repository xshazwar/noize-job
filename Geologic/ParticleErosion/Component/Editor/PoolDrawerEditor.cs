using UnityEngine;
using UnityEditor;

using xshazwar.noize;
using xshazwar.noize.geologic;

namespace xshazwar.noize.editor {
    [CustomEditor(typeof(PoolDrawer))]
    public class PoolDrawerEditor : Editor
    {
        SerializedProperty tex2d;
        SerializedProperty updateContinuous;
        SerializedProperty updateSingle;
        SerializedProperty mag;
        SerializedProperty talusAngle;
        SerializedProperty thermalStepSize;
        SerializedProperty thermalErosion;
        Texture2D texture;

        void OnEnable(){
            tex2d = serializedObject.FindProperty("texture");
            updateContinuous = serializedObject.FindProperty("updateContinuous");
            updateSingle = serializedObject.FindProperty("updateSingle");
            mag = serializedObject.FindProperty("magnitude");
            talusAngle = serializedObject.FindProperty("talusAngle");
            thermalStepSize = serializedObject.FindProperty("thermalStepSize");
            thermalErosion = serializedObject.FindProperty("thermalErosion");
            texture = tex2d.objectReferenceValue as Texture2D;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(thermalErosion);
            EditorGUILayout.PropertyField(talusAngle);
            EditorGUILayout.PropertyField(thermalStepSize);
            EditorGUILayout.PropertyField(mag);
            EditorGUILayout.PropertyField(updateContinuous);
            EditorGUILayout.PropertyField(updateSingle);
            // this is cheesy but it works
            // sticking the texture into a label didn't
            Rect space = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawPreviewTexture(space, texture);
            EditorGUILayout.TextArea("", GUIStyle.none, GUILayout.Height(1024));
            EditorGUILayout.EndHorizontal();
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}