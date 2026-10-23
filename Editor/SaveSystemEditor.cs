#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    /// Automatically verifies and enforces Unity Script Execution Order for SaveSystem, Blackboard, BlackboardScope, and BlackboardClient on Editor load.
    [InitializeOnLoad]
    public static class BlackboardExecutionOrderEnforcer{
        static BlackboardExecutionOrderEnforcer(){
            EnforceOrder<SaveSystem>(-1200);
            EnforceOrder<Blackboard>(-1100);
            EnforceOrder<BlackboardScope>(-1000);
            EnforceOrder<BlackboardClient>(0);
        }

        private static void EnforceOrder<T>(int targetOrder) where T : MonoBehaviour{
            foreach (MonoScript script in MonoImporter.GetAllRuntimeMonoScripts()){
                if (script == null || script.GetClass() != typeof(T)) continue;
                if (MonoImporter.GetExecutionOrder(script) != targetOrder)
                    MonoImporter.SetExecutionOrder(script, targetOrder);
                break;
            }
        }
    }

    /// Custom Inspector for SaveSystem providing slot selection dropdowns and one-click save execution.
    [CustomEditor(typeof(SaveSystem))]
    public class SaveSystemEditor : UnityEditor.Editor{
        private string[] _availableSlots = Array.Empty<string>();

        private void OnEnable(){
            RefreshAvailableSlots();
        }

        private void RefreshAvailableSlots(){
            List<string> slots = new() { "[Custom / New Slot...]" };

            try{
                string savesPath = Path.Combine(Application.persistentDataPath, "Saves");
                if (Directory.Exists(savesPath)){
                    string[] dirs = Directory.GetDirectories(savesPath);
                    foreach (string dir in dirs){
                        string dirName = Path.GetFileName(dir);
                        if (!string.Equals(dirName, "temp", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(dirName, "_active_session", StringComparison.OrdinalIgnoreCase)){
                            slots.Add(dirName);
                        }
                    }
                }
            }
            catch (Exception){
                // Fallback if persistentDataPath is inaccessible during compilation
            }

            _availableSlots = slots.ToArray();
        }

        public override void OnInspectorGUI(){
            DrawDefaultInspector();

            SaveSystem saveSystem = (SaveSystem)target;
            SerializedProperty targetSlotProp = serializedObject.FindProperty("targetSaveSlotName");

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Save Template & Slot Tools", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)){
                // Slot Dropdown
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Quick Slot Select");
                if (GUILayout.Button("Refresh", GUILayout.Width(60))){
                    RefreshAvailableSlots();
                }
                EditorGUILayout.EndHorizontal();

                int currentIndex = 0;
                string currentTarget = targetSlotProp != null ? targetSlotProp.stringValue : saveSystem.TargetSaveSlotName;
                if (!string.IsNullOrEmpty(currentTarget)){
                    int match = Array.IndexOf(_availableSlots, currentTarget);
                    if (match >= 0) currentIndex = match;
                }

                int newIndex = EditorGUILayout.Popup("Existing Slots", currentIndex, _availableSlots);
                if (newIndex != currentIndex && newIndex > 0){
                    if (targetSlotProp != null){
                        targetSlotProp.stringValue = _availableSlots[newIndex];
                        serializedObject.ApplyModifiedProperties();
                    }
                    else
                        saveSystem.TargetSaveSlotName = _availableSlots[newIndex];
                }

                EditorGUILayout.Space(4);

                // Save Execution
                EditorGUI.BeginDisabledGroup(!Application.isPlaying);
                GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                string buttonLabel = Application.isPlaying
                    ? $"Save Current State to '{saveSystem.TargetSaveSlotName}'"
                    : "Save to Slot (Play Mode Only)";

                if (GUILayout.Button(buttonLabel, GUILayout.Height(30))){
                    Debug.Log("THIS FEATURE IS NOT IMPLEMENTED!!");
                }
                GUI.backgroundColor = Color.white;
                EditorGUI.EndDisabledGroup();

                if (!Application.isPlaying){
                    EditorGUILayout.HelpBox("Enter Play Mode to capture active session state and commit to this save slot.", MessageType.Info);
                }
            }
        }
    }
}
#endif
