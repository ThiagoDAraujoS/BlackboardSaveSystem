using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Partition = System.Collections.Generic.Dictionary<string, object>;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    /// Contract for game components requiring state persistence.
    [MovedFrom(true, "Core.State")]
    public interface IBoundState {
        void OnLoadState(Partition state);
        void OnSaveState(Partition state);
    }

    /// Leaf lifecycle mediator that binds local IBoundState components to a BlackboardFile.
    [MovedFrom(true, "Core.State")]
    [RequireComponent(typeof(UniqueId))]
    [DefaultExecutionOrder(0)]
    public class BlackboardClient : MonoBehaviour {
        private IBoundState[]  _boundStates;
        private BlackboardFile _file;

        public string fileName = string.Empty;

        public string EntityId { get; private set; }

        /// Caches the entity UniqueId and all local IBoundState components.
        private void Awake() {
            EntityId     = GetComponent<UniqueId>().Id;
            _boundStates = GetComponents<IBoundState>();
        }

        /// Binds to the target BlackboardFile's unloaded event and loads the initial component state.
        private void OnEnable() {
            _file          =  SaveSystem.Blackboard.GetOrCreateFile(fileName);
            _file.OnUnload += FlushStateToBlackboard;
            LoadStateFromBlackboard();
        }

        /// Flushes state if the file is still loaded in the Blackboard and unsubscribes from unloading.
        private void OnDisable() {
            _file.OnUnload -= FlushStateToBlackboard;
            if (SaveSystem.Blackboard.Contains(fileName))
                FlushStateToBlackboard();
            _file = null;
        }

        /// Flushes all local IBoundState components into this entity's component-indexed partitions.
        public void FlushStateToBlackboard() {
            for (int i = 0; i < _boundStates.Length; i++)
                _boundStates[i].OnSaveState(GetPartition(i));
        }

        /// Restores all local IBoundState components from this entity's component-indexed partitions.
        public void LoadStateFromBlackboard() {
            for (int i = 0; i < _boundStates.Length; i++)
                _boundStates[i].OnLoadState(GetPartition(i));
        }

        /// Returns the partition for this entity by numeric component index.
        public Partition GetPartition(int componentIndex = 0) => GetPartition(componentIndex.ToString());

        /// Returns the partition for this entity by string component identifier.
        public Partition GetPartition(string componentId) => (_file ?? SaveSystem.Blackboard.GetOrCreateFile(fileName)).GetPartition(EntityId, componentId);
    }
}