using UnityEngine;
using UnityEditor;

using xshazwar.noize;
using xshazwar.noize.geologic;

namespace xshazwar.noize.editor {
    [CustomEditor(typeof(PoolDrawer))]
    public class PoolDrawerEditor : Editor
    {
        SerializedProperty tex2d;
        SerializedProperty run;
        SerializedProperty mag;
        Texture2D texture;

        void OnEnable(){
            tex2d = serializedObject.FindProperty("texture");
            run = serializedObject.FindProperty("updateWater");
            mag = serializedObject.FindProperty("magnitude");
            texture = tex2d.objectReferenceValue as Texture2D;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(mag);
            EditorGUILayout.PropertyField(run);
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