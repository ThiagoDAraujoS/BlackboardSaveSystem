using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    /// RAII MonoBehaviour smart pointer that anchors configured Blackboard files in RAM while enabled.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public class BlackboardScope : MonoBehaviour {
        [Tooltip("Blackboard files to keep loaded in RAM while this component is enabled.")]
        public List<string> fileNames = new();

        private readonly List<string> _acquiredFiles = new();
        private          bool         _isAcquired;

        /// Acquires configured file leases when enabled.
        private void OnEnable() => AcquireScope();

        /// Releases acquired file leases when disabled.
        private void OnDisable() => _ = ReleaseScopeAsync();

        /// Acquires references for all configured files and subscribes to Blackboard deactivation.
        public void AcquireScope() {
            if (_isAcquired) return;
            _isAcquired = true;
            _acquiredFiles.Clear();

            if (fileNames.Count == 0 && TryGetComponent(out BlackboardClient siblingClient))
                fileNames.Add(siblingClient.fileName);

            foreach (string fileName in fileNames) {
                _acquiredFiles.Add(fileName);
                SaveSystem.AcquireFileSync(fileName);
            }

            SaveSystem.Blackboard.OnDeactivate += DeactivateScope;
        }

        /// Callback triggered when the Blackboard itself deactivates.
        private void DeactivateScope() => _ = ReleaseScopeAsync();

        /// Releases references for all acquired files and unsubscribes from Blackboard deactivation.
        public async Task ReleaseScopeAsync() {
            if (!_isAcquired) return;
            _isAcquired = false;

            SaveSystem.Blackboard.OnDeactivate -= DeactivateScope;

            List<string> toRelease = new(_acquiredFiles);
            _acquiredFiles.Clear();

            await SaveSystem.ReleaseFilesAsync(toRelease);
        }
    }
}
