using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Partition = System.Collections.Generic.Dictionary<string, object>;
using EntityContainer = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, object>>;
using FileContainer = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, object>>>;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    /// Central reference-counted store managing active BlackboardFile instances and scope deactivation.
    [MovedFrom(true, "Core.Managers")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1100)]
    public class Blackboard : MonoBehaviour {
        public Dictionary<string, BlackboardFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public event Action OnDeactivate;

        /// Broadcasts deactivation to all subscribed BlackboardScopes when disabled.
        private void OnDisable() => OnDeactivate?.Invoke();

        /// Returns the current reference count for the specified file.
        public int GetRefCount(string fileName) => Files.TryGetValue(fileName, out BlackboardFile file) ? file.RefCount : 0;

        /// Retrieves an active BlackboardFile or creates a new one if not yet loaded.
        public BlackboardFile GetOrCreateFile(string fileName) {
            if (Files.TryGetValue(fileName, out BlackboardFile file)) return file;
            file            = new BlackboardFile(fileName);
            Files[fileName] = file;
            return file;
        }

        /// Retrieves or creates the EntityContainer for the specified entity ID inside a file.
        public EntityContainer GetOrCreateEntity(string fileName, string entityId) => GetOrCreateFile(fileName).GetOrCreateEntity(entityId);

        /// Alias for GetOrCreateEntity.
        public EntityContainer GetEntityContainer(string fileName, string entityId) => GetOrCreateEntity(fileName, entityId);

        /// Retrieves or creates the dictionary partition for an entity component.
        public Partition GetPartition(string fileName, string entityId, string componentId = "0") =>
            GetOrCreateFile(fileName).GetPartition(entityId, componentId);

        /// Clears all loaded files from memory.
        public void Clear() => Files.Clear();

        /// Broadcasts data collection for a specific file if currently loaded.
        public void CollectDataForFile(string fileName) {
            if (Files.TryGetValue(fileName, out BlackboardFile file))
                file.CollectData();
        }

        /// Broadcasts data collection across all loaded files.
        public void CollectAllData() {
            foreach (BlackboardFile file in Files.Values)
                file.CollectData();
        }

        /// Returns true if the specified file is currently loaded in RAM.
        public bool Contains(string fileName) => Files.ContainsKey(fileName);

        /// Decrements the reference count for a file, unloading and removing it from memory if reaching 0.
        public bool ReleaseFile(string fileName) {
            if (!Files.TryGetValue(fileName, out BlackboardFile file)) return false;
            if (--file.RefCount > 0) return false;

            file.NotifyUnloading();
            return Files.Remove(fileName);
        }

        /// Decrements reference count for a file; when reaching 0, unloads clients, removes from RAM, and serializes to disk.
        public async Task<bool> ReleaseFileAsync(string fileName, Action<string> onFailure = null) {
            if (!Files.TryGetValue(fileName, out BlackboardFile file)) return false;
            if (--file.RefCount > 0) return false;

            file.NotifyUnloading();
            Files.Remove(fileName);
            await file.SerializeAsync(onFailure);
            return true;
        }

        /// Decrements reference counts for multiple files, unloading, removing, and serializing those reaching 0.
        public async Task ReleaseFilesAsync(IEnumerable<string> fileNames, Action<string> onFailure = null) {
            List<BlackboardFile> filesToSerialize = new();

            foreach (string fileName in fileNames) {
                if (!Files.TryGetValue(fileName, out BlackboardFile file)) continue;
                if (--file.RefCount > 0) continue;

                file.NotifyUnloading();
                Files.Remove(fileName);
                filesToSerialize.Add(file);
            }

            if (filesToSerialize.Count == 0) return;

            List<Task> serializeTasks = filesToSerialize.Select(f => f.SerializeAsync(onFailure)).ToList();
            await Task.WhenAll(serializeTasks);
        }

        /// Synchronously acquires a reference to a file, reading from disk only if not already loaded in memory.
        public void AcquireFileSync(string fileName, Action<string> onFailure = null) {
            if (Files.TryGetValue(fileName, out BlackboardFile existing)) {
                existing.RefCount++;
                return;
            }

            FileContainer data = BlackboardFile.ReadFromDisk(fileName, onFailure);
            Files[fileName] = new BlackboardFile(fileName, data) { RefCount = 1 };
        }

        /// Asynchronously acquires references and deserializes multiple save files in parallel.
        public Task AcquireFilesAsync(IEnumerable<string> fileNames, Action<string> onFailure = null) =>
            DeserializeFiles(fileNames, onFailure);

        /// Serializes a single loaded file to the active session folder on disk.
        public async Task SerializeFile(string fileName, Action<string> onFailure = null) {
            if (!Files.TryGetValue(fileName, out BlackboardFile file)) return;
            await file.SerializeAsync(onFailure);
        }

        /// Serializes all active files in parallel into the active session working folder.
        public async Task SerializeBoard(Action<string> onFailure = null) {
            List<Task> tasks = Files.Values.Select(file => file.SerializeAsync(onFailure)).ToList();
            await Task.WhenAll(tasks);
        }

        /// Deserializes multiple save files in parallel on background worker threads and increments reference counts.
        public async Task DeserializeFiles(IEnumerable<string> fileNames, Action<string> onFailure = null) {
            List<string> filesToLoad = new();
            foreach (string fileName in fileNames) {
                if (Files.TryGetValue(fileName, out BlackboardFile existing)) {
                    existing.RefCount++;
                    continue;
                }

                if (!filesToLoad.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    filesToLoad.Add(fileName);
            }

            if (filesToLoad.Count == 0) return;

            List<Task<(string Name, FileContainer Data)>> tasks = filesToLoad.Select(fileName => Task.Run(() => {
                FileContainer diskData = BlackboardFile.ReadFromDisk(fileName, onFailure);
                return (fileName, diskData);
            })).ToList();

            (string Name, FileContainer Data)[] results = await Task.WhenAll(tasks);

            foreach ((string fileName, FileContainer data) in results)
                if (data != null && !Files.ContainsKey(fileName))
                    Files[fileName] = new BlackboardFile(fileName, data) { RefCount = 1 };
        }

        /// Custom JsonConverter that preserves primitive numeric types and 4-level partition dictionaries.
        public class SafeNumericConverter : JsonConverter {
            /// Returns true if the target type is a supported Blackboard container or object.
            public override bool CanConvert(Type objectType) =>
                objectType == typeof(object) ||
                objectType == typeof(Partition) ||
                objectType == typeof(EntityContainer) ||
                objectType == typeof(FileContainer);

            /// Deserializes JSON tokens into case-insensitive Blackboard containers and strongly typed primitives.
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
                JToken token = JToken.Load(reader);
                if (token.Type == JTokenType.Null) return null;

                if (objectType == typeof(FileContainer) && token is JObject fileObj)
                    return ConvertFileContainer(fileObj);

                if (objectType == typeof(EntityContainer) && token is JObject entityObj)
                    return ConvertEntityContainer(entityObj);

                if (objectType == typeof(Partition) && token is JObject partitionObj)
                    return ConvertObject(partitionObj);

                return ReadToken(token);
            }

            /// Converts a root JObject into a FileContainer dictionary.
            private FileContainer ConvertFileContainer(JObject fileObj) {
                FileContainer fileContainer = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, JToken> entityProp in fileObj)
                    if (entityProp.Value is JObject entityObj)
                        fileContainer[entityProp.Key] = ConvertEntityContainer(entityObj);

                return fileContainer;
            }

            /// Converts an entity JObject into an EntityContainer dictionary.
            private EntityContainer ConvertEntityContainer(JObject entityObj) {
                EntityContainer entityContainer = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, JToken> compProp in entityObj) {
                    if (compProp.Value is JObject compObj)
                        entityContainer[compProp.Key] = ConvertObject(compObj);
                    else {
                        if (!entityContainer.TryGetValue("0", out Partition defaultPartition)) {
                            defaultPartition     = new Partition(StringComparer.OrdinalIgnoreCase);
                            entityContainer["0"] = defaultPartition;
                        }

                        defaultPartition[compProp.Key] = ReadToken(compProp.Value);
                    }
                }

                return entityContainer;
            }

            /// Recursively converts a JToken into a Partition, List, or primitive value.
            private object ReadToken(JToken token) {
                return token.Type switch {
                    JTokenType.Object => ConvertObject((JObject)token),
                    JTokenType.Array  => ConvertArray((JArray)token),
                    JTokenType.Null   => null,
                    _                 => ParsePrimitiveValue()
                };

                object ParsePrimitiveValue() {
                    object value = token.ToObject<object>();
                    if (value == null) return null;

                    return token.Type switch {
                        JTokenType.Integer => Convert.ToInt32(value),
                        JTokenType.Float   => Convert.ToSingle(value),
                        JTokenType.Boolean => (bool)value,
                        JTokenType.String  => (string)value,
                        _                  => value
                    };
                }
            }

            /// Converts a JObject into a case-insensitive Partition dictionary.
            private Partition ConvertObject(JObject obj) {
                Partition dict = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, JToken> prop in obj)
                    dict[prop.Key] = ReadToken(prop.Value);
                return dict;
            }

            /// Converts a JArray into a List of converted token objects.
            private object ConvertArray(JArray arr) => arr.Select(ReadToken).ToList();

            /// Throws NotImplementedException as this converter is read-only.
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotImplementedException();

            public override bool CanWrite => false;
        }
    }
}
