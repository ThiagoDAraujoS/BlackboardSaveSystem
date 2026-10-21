using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem{
    /// RAII MonoBehaviour that acquires reference-counted leases on configured Blackboard files while enabled
    /// and releases them when disabled.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public class BlackboardScope : MonoBehaviour{
        [Tooltip("Blackboard files to keep loaded in RAM while this component is enabled.")]
        public List<string> fileNames = new();

        private readonly List<string> _acquiredFiles = new();
        private          bool         _isAcquired;

        private void OnEnable() => AcquireScope();

        private void OnDisable() => _ = ReleaseScopeAsync();

        /// Acquires references for all configured files, loading any not yet in RAM from the active session directory.
        public void AcquireScope(){
            if (_isAcquired || SaveSystem.Instance == null) return;

            _isAcquired = true;
            _acquiredFiles.Clear();

            if ((fileNames == null || fileNames.Count == 0) &&
                TryGetComponent(out BlackboardClient siblingClient) &&
                !string.IsNullOrWhiteSpace(siblingClient.fileName)){
                fileNames ??= new List<string>();
                fileNames.Add(siblingClient.fileName);
            }

            if (fileNames == null) return;

            foreach (string fileName in fileNames){
                if (string.IsNullOrWhiteSpace(fileName)) continue;

                _acquiredFiles.Add(fileName);
                SaveSystem.AcquireFileSync(fileName);
            }
        }

        /// Releases references for all files acquired by this scope, awaiting disk serialization for any file whose reference count reaches 0.
        public async Task ReleaseScopeAsync(){
            if (!_isAcquired) return;
            _isAcquired = false;

            if (SaveSystem.Instance == null || _acquiredFiles.Count == 0){
                _acquiredFiles.Clear();
                return;
            }

            List<string> toRelease = new(_acquiredFiles);
            _acquiredFiles.Clear();

            await SaveSystem.ReleaseFilesAsync(toRelease);
        }
    }
}
