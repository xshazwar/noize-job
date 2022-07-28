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

        void OnEnable(){
            tex2d = serializedObject.FindProperty("texture");
            run = serializedObject.FindProperty("updateWater");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Texture2D texture = tex2d.objectReferenceValue as Texture2D;
            // EditorGUI.DrawPreviewTexture(new Rect(240, 140, 512, 512), texture);
            EditorGUILayout.PropertyField(run);
            GUILayout.Label(texture);
            serializedObject.ApplyModifiedProperties();
        }
    }
}