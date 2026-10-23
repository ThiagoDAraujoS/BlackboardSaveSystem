using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
// ReSharper disable InconsistentNaming

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    [MovedFrom(true, "Core.Managers")]
    [RequireComponent(typeof(Blackboard))]
    [DefaultExecutionOrder(-1200)]
    public class SaveSystem : MonoBehaviour{
        private Blackboard _blackboard;

        public static SaveSystem Instance{ get; private set; }

        public static Blackboard Blackboard => Instance._blackboard;
        public        string[]   coreFileNames = { "core" };

        public static Func<Dictionary<string, string>> MetadataProvider        { get; set; }

        [SerializeField] private string currentSaveSlot;
        public static            string CurrentSaveSlot          => Instance.currentSaveSlot;
        public static            string CurrentSaveSlotDirectory => Path.Combine(_baseSavePath, CurrentSaveSlot);
        
        [SerializeField] private string targetSaveSlotName;
        public string TargetSaveSlotName{
            get => targetSaveSlotName;
            set => targetSaveSlotName = value;
        }

        private const string TempDirectoryName          = "temp";
        private const string ActiveSessionDirectoryName = "_active_session";

        public static string TempDirectory          => Path.Combine(_baseSavePath, TempDirectoryName);
        public static string ActiveSessionDirectory => _activeSessionDirectory;

        private static string _baseSavePath;
        private static string _activeSessionDirectory;

        public void Awake(){
            if (Instance != null && Instance != this){
                Destroy(gameObject);
                return;
            }

            Instance                = this;
            _baseSavePath           = Path.Combine(Application.persistentDataPath, "Saves");
            _activeSessionDirectory = Path.Combine(Application.persistentDataPath, ActiveSessionDirectoryName);

            if (!Directory.Exists(_baseSavePath))
                Directory.CreateDirectory(_baseSavePath);

            _blackboard = gameObject.GetComponent<Blackboard>();
        }

        private void OnDestroy(){
            if (Instance == this)
                Instance = null;
        }

        /// Configures the active save slot folder. Creates the directory if it does not exist.
        public static void SetSaveSlot(string slotName){
            Instance.currentSaveSlot = slotName;
            string directory = CurrentSaveSlotDirectory;
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        /// Copies files from the selected source save slot directly into the active session working folder.
        public static void InitializeSession(string sourceSlotName = null){
            if (Directory.Exists(ActiveSessionDirectory))
                Directory.Delete(ActiveSessionDirectory, true);
            Directory.CreateDirectory(ActiveSessionDirectory);
            
            if (string.IsNullOrEmpty(sourceSlotName))
                Instance.currentSaveSlot = $"NewGame_{DateTime.Now:yyyyMMdd_HHmmss}";
            else{
                Instance.currentSaveSlot = sourceSlotName;
                string sourceDir = Path.Combine(_baseSavePath, sourceSlotName);
                if (Directory.Exists(sourceDir))
                    CopyDirectory(sourceDir, ActiveSessionDirectory);
            }
        }

        /// Flushes active entities and commits all currently loaded Blackboard partitions to the active session folder on disk.
        public static async Task CommitToActiveSessionAsync(Action<string> onFailure = null){
            Blackboard.CollectAllData();
            await Blackboard.SerializeBoard(onFailure);
        }

        /// Flushes active entities and serializes a specific partition to disk.
        public static async Task CommitFileAsync(string fileName){
            Blackboard.CollectDataForFile(fileName);
            await Blackboard.SerializeFile(fileName);
        }

        /// Completely purges all loaded partitions from active memory.
        public static void ClearActiveMemory() => Blackboard.Clear();

        /// Returns the current reference count for a loaded file.
        public static int GetRefCount(string fileName) => Blackboard.GetRefCount(fileName);

        /// Synchronously acquires a file reference and loads it from the active session directory if not yet in memory.
        public static void AcquireFileSync(string fileName, Action<string> onFailure = null) =>
            Blackboard.AcquireFileSync(fileName, onFailure);

        /// Synchronously acquires multiple file references and loads any missing files from the active session directory.
        public static void AcquireFilesSync(IEnumerable<string> fileNames, Action<string> onFailure = null){
            foreach (string fileName in fileNames)
                Blackboard.AcquireFileSync(fileName, onFailure);
        }

        /// Decrements the reference count for a file and removes it from active memory only if the reference count reaches 0.
        public static bool ReleaseFile(string fileName) => Blackboard.ReleaseFile(fileName);

        /// Decrements the reference count for a file, flushing clients, serializing, and removing it from memory if reaching 0.
        public static Task<bool> ReleaseFileAsync(string fileName, Action<string> onFailure = null) =>
            Blackboard.ReleaseFileAsync(fileName, onFailure);

        /// Decrements reference counts for multiple files, flushing, serializing, and removing only those whose reference count reaches 0.
        public static Task ReleaseFilesAsync(IEnumerable<string> fileNames, Action<string> onFailure = null) =>
            Blackboard.ReleaseFilesAsync(fileNames, onFailure);

        /// Alias for ReleaseFileAsync to unload a ref-counted file partition.
        public static Task<bool> UnloadFileAsync(string fileName, Action<string> onFailure = null) =>
            ReleaseFileAsync(fileName, onFailure);

        /// Alias for ReleaseFilesAsync to unload multiple ref-counted file partitions.
        public static Task UnloadFilesAsync(IEnumerable<string> fileNames, Action<string> onFailure = null) =>
            ReleaseFilesAsync(fileNames, onFailure);

        /// Commits active RAM state to the working session and copies the session folder to the target save slot.
        public static async Task SaveGame(string targetSlotName = null, Action<string> onFailure = null){
            try{
                if (!string.IsNullOrEmpty(targetSlotName))
                    Instance.currentSaveSlot = targetSlotName;

                await CommitToActiveSessionAsync(onFailure);

                Dictionary<string, string> variables = MetadataProvider?.Invoke() ?? new Dictionary<string, string>();
                SaveFileMetadata           meta      = new(CurrentSaveSlot, DateTime.Now, variables);

                string metaJson     = JsonConvert.SerializeObject(meta, Formatting.Indented);
                string metaFilePath = Path.Combine(ActiveSessionDirectory, "meta.json");
                await File.WriteAllTextAsync(metaFilePath, metaJson);

                if (Directory.Exists(TempDirectory))
                    Directory.Delete(TempDirectory, true);

                CopyDirectory(ActiveSessionDirectory, TempDirectory);

                if (Directory.Exists(CurrentSaveSlotDirectory))
                    Directory.Delete(CurrentSaveSlotDirectory, true);

                Directory.Move(TempDirectory, CurrentSaveSlotDirectory);
            }
            catch (Exception){
                if (Directory.Exists(TempDirectory))
                    Directory.Delete(TempDirectory, true);
            }
        }

        /// Helper method to recursively copy all files from the source directory to the destination directory.
        private static void CopyDirectory(string sourceDir, string destinationDir){
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);

            foreach (string subDir in Directory.GetDirectories(sourceDir))
                CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
        }

        /// Loads a specific Blackboard partition on demand from the active session directory.
        public static async Task LoadFile(string fileName, Action<string> onFailure = null) =>
            await Instance._blackboard.DeserializeFiles(new[]{ fileName }, onFailure);

        /// Loads multiple Blackboard partitions in parallel on demand from the active session directory.
        public static async Task LoadFiles(IEnumerable<string> fileNames, Action<string> onFailure = null){
            if (fileNames == null) return;
            await Instance._blackboard.DeserializeFiles(fileNames, onFailure);
        }

        /// Deletes a save slot directory and its contents from the disk.
        public static bool DeleteSave(string slotName){
            if (string.IsNullOrEmpty(slotName)) return false;
            if (string.Equals(slotName, TempDirectoryName, StringComparison.OrdinalIgnoreCase)) return false;
            if (Instance != null) return false;

            _baseSavePath ??= Path.Combine(Application.persistentDataPath, "Saves");
            string targetDir = Path.Combine(_baseSavePath, slotName);
            if (!Directory.Exists(targetDir)) return false;
            Directory.Delete(targetDir, true);
            return true;
        }

        /// Gathers all save folders from the disk and reconstructs their metadata, sorting newest first.
        public static List<SaveFileMetadata> GetSaveFileList(){
            List<SaveFileMetadata> saveList = new();

            foreach (string dirPath in Directory.GetDirectories(_baseSavePath)){
                string dirName = Path.GetFileName(dirPath);
                if (string.Equals(dirName, TempDirectoryName, StringComparison.OrdinalIgnoreCase)) continue;
                saveList.Add(ReadMetadata(dirPath, dirName));
            }

            saveList.Sort((a, b) => b.lastSaveTime.CompareTo(a.lastSaveTime));
            return saveList;
        }

        /// Reads meta.json for a slot directory or returns a void SaveFileMetadata object if unreadable.
        private static SaveFileMetadata ReadMetadata(string dirPath, string dirName){
            string metaFilePath = Path.Combine(dirPath, "meta.json");
            if (!File.Exists(metaFilePath)) return SaveFileMetadata.CreateVoid(dirName);

            SaveFileMetadata meta = JsonConvert.DeserializeObject<SaveFileMetadata>(File.ReadAllText(metaFilePath));
            meta.slotName  = string.IsNullOrEmpty(meta.slotName) ? dirName : meta.slotName;
            meta.Variables ??= new Dictionary<string, string>();
            return meta;
        }

        /// Automatically performs an atomic save to one of three rolling autosave directories,
        /// overwriting the oldest existing autosave slot.
        public static async Task AutosaveAsync(Action<string> onFailure = null){
            DateTime oldestTime = DateTime.MaxValue;
            string   targetName = "autosave_00";

            for (int i = 0; i < 3; i++){
                string baseName = $"autosave_{i:D2}";
                string path     = Path.Combine(_baseSavePath, baseName);

                if (!Directory.Exists(path)){
                    targetName = baseName;
                    break;
                }

                DateTime writeTime = Directory.GetLastWriteTimeUtc(path);
                if (writeTime >= oldestTime)
                    continue;

                oldestTime = writeTime;
                targetName = baseName;
            }

            await SaveGame(targetName, onFailure);
        }

#if UNITY_EDITOR
        [ContextMenu("AutoSave")]
        public void AutoSave() => _ = AutosaveAsync();
#endif
    }

    /// Serializable metadata container for a save slot directory using the Null Object pattern for missing values.
    [Serializable]
    public struct SaveFileMetadata{
        public const string VoidValue = "VOID";

        public string                     slotName;
        public DateTime                   lastSaveTime;
        [NonSerialized]
        public Dictionary<string, string> Variables;

        /// Initializes save slot metadata with a slot name, timestamp, and key-value variables.
        public SaveFileMetadata(string slotName, DateTime lastSaveTime, Dictionary<string, string> variables = null){
            this.slotName     = string.IsNullOrEmpty(slotName) ? VoidValue : slotName;
            this.lastSaveTime = lastSaveTime;
            Variables         = variables ?? new Dictionary<string, string>();
        }

        /// Returns a Null Object metadata instance for unreadable or missing save metadata.
        public static SaveFileMetadata CreateVoid(string slotName = VoidValue) =>
            new(slotName, DateTime.MinValue, new Dictionary<string, string>());

        /// Retrieves a metadata variable by key, returning "VOID" if the key could not be read.
        public string Get(string key) =>
            Variables != null && Variables.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : VoidValue;
    }
}
