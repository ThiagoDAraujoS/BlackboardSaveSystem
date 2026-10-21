using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Partition = System.Collections.Generic.Dictionary<string, object>;
using EntityContainer = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, object>>;
using FileContainer = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, object>>>;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    /// Encapsulates a single save file's in-memory dictionary, reference count, and unload lifecycle events.
    public class BlackboardFile {
        public readonly string        name;
        public readonly FileContainer data;

        public int RefCount { get; set; }

        public event Action OnUnload;

        /// Initializes a new BlackboardFile with the specified file name and optional initial dictionary data.
        public BlackboardFile(string name, FileContainer data = null) {
            this.name = name;
            this.data = data ?? new FileContainer(StringComparer.OrdinalIgnoreCase);
        }

        /// Invokes the unloaded event so subscribed clients flush their state into this file.
        public void CollectData() => OnUnload?.Invoke();

        /// Triggers client data collection and clears all unloaded subscribers as the file unloads.
        public void NotifyUnloading() {
            OnUnload?.Invoke();
            OnUnload = null;
        }

        /// Retrieves or creates the EntityContainer for the specified entity ID.
        public EntityContainer GetOrCreateEntity(string entityId) {
            lock (data){
                if (data.TryGetValue(entityId, out EntityContainer entity)) return entity;
                entity         = new EntityContainer(StringComparer.OrdinalIgnoreCase);
                data[entityId] = entity;
                return entity;
            }
        }

        /// Retrieves or creates the Partition dictionary for the specified entity and component ID.
        public Partition GetPartition(string entityId, string componentId = "0") {
            EntityContainer entity = GetOrCreateEntity(entityId);
            if (entity.TryGetValue(componentId, out Partition partition)) return partition;
            partition           = new Partition(StringComparer.OrdinalIgnoreCase);
            entity[componentId] = partition;
            return partition;
        }

        /// Resolves the full file path in the active session directory for the given file name.
        public static string GetSessionFilePath(string fileName) => Path.Combine(SaveSystem.ActiveSessionDirectory, $"{fileName}.json");

        /// Serializes this file's dictionary data to the active session folder on disk.
        public async Task SerializeAsync(Action<string> onFailure = null) {
            string filePath = GetSessionFilePath(name);
            await Task.Run(() => {
                try {
                    string json;
                    lock (data)
                        json = JsonConvert.SerializeObject(data, Formatting.Indented);

                    File.WriteAllText(filePath, json);
                }
                catch (Exception e) {
                    onFailure?.Invoke($"Failed writing session file {name}: {e.Message}");
                }
            });
        }

        /// Reads and deserializes a FileContainer from the active session directory on disk.
        public static FileContainer ReadFromDisk(string fileName, Action<string> onFailure = null) {
            if (string.IsNullOrEmpty(SaveSystem.ActiveSessionDirectory))
                return new FileContainer(StringComparer.OrdinalIgnoreCase);

            string filePath = GetSessionFilePath(fileName);

            if (!File.Exists(filePath)) {
                try {
                    string dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(filePath, "{}");
                }
                catch (Exception ex) {
                    onFailure?.Invoke($"Failed to create empty file {fileName}: {ex.Message}");
                }

                return new FileContainer(StringComparer.OrdinalIgnoreCase);
            }

            try {
                string        json     = File.ReadAllText(filePath);
                FileContainer diskData = JsonConvert.DeserializeObject<FileContainer>(json, new Blackboard.SafeNumericConverter());
                return diskData ?? new FileContainer(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e) {
                onFailure?.Invoke($"Session file {fileName} is corrupted: {e.Message}");
                return null;
            }
        }
    }
}
