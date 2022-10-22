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
        SerializedProperty talusAngle;
        SerializedProperty thermalStepSize;
        SerializedProperty thermalCyclesPerCycle;
        SerializedProperty thermalErosion;
        SerializedProperty reset;
        SerializedProperty showMap;
        SerializedProperty performErosion;
        SerializedProperty erosionCycles;
        Texture2D texture;

        void OnEnable(){
            tex2d = serializedObject.FindProperty("texture");
            updateContinuous = serializedObject.FindProperty("updateContinuous");
            updateSingle = serializedObject.FindProperty("updateSingle");
            talusAngle = serializedObject.FindProperty("talusAngle");
            thermalStepSize = serializedObject.FindProperty("thermalStepSize");
            thermalCyclesPerCycle = serializedObject.FindProperty("thermalCyclesPerCycle");
            thermalErosion = serializedObject.FindProperty("thermalErosion");
            reset = serializedObject.FindProperty("resetLand");
            showMap = serializedObject.FindProperty("showMap");
            performErosion = serializedObject.FindProperty("performErosion");
            erosionCycles = serializedObject.FindProperty("Cycles");
            texture = tex2d.objectReferenceValue as Texture2D;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(erosionCycles);
            EditorGUILayout.PropertyField(reset);
            EditorGUILayout.PropertyField(thermalErosion);
            EditorGUILayout.PropertyField(talusAngle);
            EditorGUILayout.PropertyField(thermalStepSize);
            EditorGUILayout.PropertyField(thermalCyclesPerCycle);
            EditorGUILayout.PropertyField(updateContinuous);
            EditorGUILayout.PropertyField(updateSingle);
            EditorGUILayout.PropertyField(showMap);
            EditorGUILayout.PropertyField(performErosion);
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