// using UnityEngine;
// using UnityEditor;

// using xshazwar.noize;
// using xshazwar.noize.geologic;

// namespace xshazwar.noize.editor {
//     [CustomEditor(typeof(PoolDrawer))]
//     public class PoolDrawerEditor : Editor
//     {

//         SerializedProperty tex2d;

//         void OnEnable(){
//             tex2d = serializedObject.FindProperty("texture");
//         }

//         public override void OnInspectorGUI()
//         {
//             serializedObject.Update();
//             EditorGUILayout.PropertyField(tex2d);
//             serializedObject.ApplyModifiedProperties();
//         }
//     }
// }