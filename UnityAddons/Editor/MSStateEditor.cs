// using UnityEngine;
// using UnityEditor;

// using xshazwar.noize;
// using xshazwar.noize.geologic;

// namespace xshazwar.noize.editor {
//     [CustomEditor(typeof(MSState))]
//     public class MSStateEditor : Editor
//     {

//         SerializedProperty tex2d;
//         RenderTexture target;
//         string targetTexture = "";

//         void OnEnable(){
//             tex2d = serializedObject.FindProperty("texture");
//         }

//         public override void OnInspectorGUI()
//         {
//             serializedObject.Update();
//             EditorGUILayout.PropertyField(tex2d);
            
//             serializedObject.ApplyModifiedProperties();

//             EditorGUILayout.BeginHorizontal ();
//             GUILayout.Label ("Dynamic Buffer");
//             // int mem = sm.updateBuffer.width * sm.updateBuffer.height * 2;
//             // mem /= 128;
//             // EditorGUILayout.LabelField ("Buffer Memory: " + mem.ToString () + "kb");
//             EditorGUILayout.EndHorizontal ();
//             GUILayout.Box (sm.updateBuffer.GetCurrent(), GUILayout.Width (256), GUILayout.Height (256));
//         }
//     }
// }