using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    /// <summary>
    /// Provides a persistent, globally unique identifier (UUID) for GameObjects in scenes.
    /// Handles duplication (Ctrl+D / Copy-Paste) by generating a fresh UUID for the clone.
    /// </summary>
    [MovedFrom(true, "Core.State")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class UniqueId : MonoBehaviour, ISerializationCallbackReceiver {
        [SerializeField]
        [HideInInspector]
        private string uniqueId;

        public string Id => uniqueId;

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize() { }

#if UNITY_EDITOR
        private void Awake() {
            if (Application.isPlaying) return;
            ValidateAndAssignId();
        }

        private void OnValidate() {
            ValidateAndAssignId();
        }

        private void ValidateAndAssignId() {
            // 1. Ignore if in prefab isolation/asset mode unless you deliberately want prefab assets to have IDs
            if (PrefabStageUtility.GetCurrentPrefabStage() != null || EditorUtility.IsPersistent(this))
                return;

            // 2. Generate initial ID if totally blank
            if (string.IsNullOrEmpty(uniqueId)) {
                AssignNewId();
                return;
            }

            // 3. Detect duplicate IDs across all scene instances of UniqueId
            UniqueId[] allIds = FindObjectsByType<UniqueId>(FindObjectsInactive.Include);
            foreach (UniqueId other in allIds)
                if (other != this && other != null && other.uniqueId == uniqueId) {
                    // Conflict found (copy/paste or duplicate action occurred).
                    // The object higher in instance hierarchy or recently cloned gets rerolled.
                    AssignNewId();
                    break;
                }
        }

        private void AssignNewId() {
            Undo.RecordObject(this, "Assign New Unique ID");
            uniqueId = Guid.NewGuid().ToString();
            EditorUtility.SetDirty(this);

            if (!Application.isPlaying && gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        [ContextMenu("Reroll UUID")]
        public void RerollId() {
            AssignNewId();
            Debug.Log($"Rerolled UUID for {gameObject.name}: {uniqueId}", this);
        }

        [ContextMenu("Copy UUID to Clipboard")]
        public void CopyIdToClipboard() {
            GUIUtility.systemCopyBuffer = uniqueId;
            Debug.Log($"Copied UUID to clipboard: {uniqueId}");
        }
#endif
    }
}