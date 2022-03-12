 using UnityEngine;
 using UnityEditor;
 
 [CustomPropertyDrawer(typeof(InstanceAliasAttribute))]
 public class InstanceAliasAttribute : PropertyDrawer
 {
     public override void OnGUI( Rect position, SerializedProperty property, GUIContent label )
     {
        
        // EditorGUI.PropertyField(position, property, new GUIContent( (attribute as InstanceAliasAttribute).namePrefix  ));
        // SerializedProperty container_prop = property.serializedObject.FindProperty("alias")
        string alias = property.serializedObject.FindProperty("alias")?.stringValue;
        if(alias != null) {
            EditorGUI.PropertyField(position, property, new GUIContent(alias));
        }
         
     }
 }