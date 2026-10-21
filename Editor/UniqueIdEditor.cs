#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem.Editor {
    [CustomEditor(typeof(UniqueId))]
    [CanEditMultipleObjects]
    public class UniqueIdEditor : UnityEditor.Editor {
        private SerializedProperty _uniqueIdProp;

        private static readonly Color RedButtonColor   = new(0.95f, 0.35f, 0.35f);
        private static readonly Color GreenButtonColor = new(0.35f, 0.85f, 0.45f);

        private void OnEnable() {
            _uniqueIdProp = serializedObject.FindProperty("uniqueId");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            UniqueId uniqueIdComponent = (UniqueId)target;

            // 1. Uneditable field containing the UUID (using SelectableLabel or disabled TextField)
            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.PropertyField(_uniqueIdProp, new GUIContent("UUID"));
            }

            EditorGUILayout.Space(4f);

            // Horizontal row for the two action buttons
            EditorGUILayout.BeginHorizontal();
            {
                Color originalBgColor = GUI.backgroundColor;

                // 2. Red button that runs RerollId()
                GUI.backgroundColor = RedButtonColor;
                if (GUILayout.Button("Reroll UUID", GUILayout.Height(24f))) {
                    foreach (Object targetObj in targets) {
                        if (targetObj is UniqueId uid) {
                            uid.RerollId();
                        }
                    }
                }

                // 3. Green button that runs CopyIdToClipboard()
                GUI.backgroundColor = GreenButtonColor;
                using (new EditorGUI.DisabledScope(targets.Length > 1)) {
                    if (GUILayout.Button("Copy UUID to Clipboard", GUILayout.Height(24f))) {
                        uniqueIdComponent.CopyIdToClipboard();
                    }
                }

                // Restore original GUI color so we don't tint the rest of the Inspector
                GUI.backgroundColor = originalBgColor;
            }
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif