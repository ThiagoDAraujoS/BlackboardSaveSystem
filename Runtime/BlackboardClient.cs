using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Partition = System.Collections.Generic.Dictionary<string, object>;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    /// Contract for game components requiring state persistence.
    /// Passes raw dictionary partitions that utilize Blackboard variables.
    [MovedFrom(true, "Core.State")]
    public interface IBoundState {
        void OnLoadState(Partition state);
        void OnSaveState(Partition state);
    }
    
    /// A lifecycle mediator that automatically binds local game components to the unified Blackboard database.
    /// It grabs the entity's UniqueId, locates all local scripts implementing IBoundState,
    /// and coordinates their loading and saving hooks into the unified board.
    /// It also acts as an on-demand proxy, allowing components and other systems to directly
    /// query active data partitions for this entity.
    [MovedFrom(true, "Core.State")]
    [RequireComponent(typeof(UniqueId))]
    [DefaultExecutionOrder(0)]
    public class BlackboardClient : MonoBehaviour {
        public static readonly HashSet<BlackboardClient> ActiveClients = new();

        private IBoundState[] _boundStates;
        
        public string fileName = string.Empty;  

        public string EntityId { get; private set; }

        private void Awake() {
            EntityId     = GetComponent<UniqueId>().Id;
            _boundStates = GetComponents<IBoundState>();
        }

        private void OnEnable() {
            ActiveClients.Add(this);
            LoadStateFromBlackboard();
        }

        private void OnDisable() {
            ActiveClients.Remove(this);
            FlushStateToBlackboard();
        }
        
        /// Flushes all local IBoundState components directly into this entity's component-indexed blackboard partitions.
        public void FlushStateToBlackboard() {
            for (int i = 0; i < _boundStates.Length; i++) {
                Partition state = GetPartition(i);
                _boundStates[i]?.OnSaveState(state);
            }
        }
        
        /// Restores all local IBoundState components directly from this entity's component-indexed blackboard partitions.
        public void LoadStateFromBlackboard() {
            for (int i = 0; i < _boundStates.Length; i++) {
                Partition state = GetPartition(i);
                _boundStates[i]?.OnLoadState(state);
            }
        }
        
        /// Shortcut accessor to get a component's specific partition for this entity on the unified board.
        /// Automatically generates a clean, empty partition if it does not exist yet.
        public Partition GetPartition(int componentIndex = 0) => SaveSystem.Blackboard.GetPartition(fileName, EntityId, componentIndex.ToString());

        /// Shortcut accessor to get a component's specific partition by string componentId for this entity on the unified board.
        public Partition GetPartition(string componentId) => SaveSystem.Blackboard.GetPartition(fileName, EntityId, componentId);
    }
}