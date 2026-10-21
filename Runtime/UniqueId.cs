using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    /// Provides a persistent, immutable, and globally unique identifier (UUID) for GameObjects.
    /// This identifier is generated strictly within the Unity Editor and serialized permanently, 
    /// acting as the database lookup key to bind local entities to the global <see cref="Blackboard"/>.
    [MovedFrom(true, "Core.State")]
    [DisallowMultipleComponent]
    public class UniqueId : MonoBehaviour {
        [SerializeField]
        [HideInInspector] 
        private string uniqueId;
        
        /// Gets the immutable, unique string identifier assigned to this entity.
        public string Id => uniqueId;

#if UNITY_EDITOR
        /// Unity editor callback invoked when the script is loaded, added, or values are changed.
        /// Ensures that a new, valid GUID is instantly generated and serialized if the current field is empty.
        private void OnValidate() {
            if (!string.IsNullOrEmpty(uniqueId)) return;
            uniqueId = Guid.NewGuid().ToString();
            UnityEditor.EditorUtility.SetDirty(this);
        }
        
        /// Forces a regeneration of the UUID. Useful when duplicating GameObjects or prefabs.
        [ContextMenu("Reroll UUID")]
        public void RerollId() {
            UnityEditor.Undo.RecordObject(this, "Reroll UUID");
            uniqueId = Guid.NewGuid().ToString();
            UnityEditor.EditorUtility.SetDirty(this);
            if (!Application.isPlaying && gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log($"Rerolled UUID for {gameObject.name}: {uniqueId}", this);
        }

        [ContextMenu("Copy UUID to Clipboard")]
        private void CopyIdToClipboard() {
            GUIUtility.systemCopyBuffer = uniqueId;
            Debug.Log($"Copied UUID to clipboard: {uniqueId}");
        }
#endif
    }
}
