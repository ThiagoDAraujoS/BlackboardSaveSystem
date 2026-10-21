using System;
using System.Collections.Generic;
using System.IO;
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
    [MovedFrom(true, "Core.Managers")]
    [DisallowMultipleComponent]
    public class Blackboard : MonoBehaviour{
        public Dictionary<string, FileContainer> Files{ get; } = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _refCounts = new(StringComparer.OrdinalIgnoreCase);

        public int GetRefCount(string fileName) =>
            !string.IsNullOrEmpty(fileName) && _refCounts.TryGetValue(fileName, out int count) ? count : 0;

        public FileContainer GetOrCreateFile(string fileName){
            if (Files.TryGetValue(fileName, out FileContainer dict)) return dict;
            dict = new FileContainer(StringComparer.OrdinalIgnoreCase);

            Files[fileName] = dict;
            return dict;
        }

        public EntityContainer GetOrCreateEntity(string fileName, string entityId){
            FileContainer fileContainer = GetOrCreateFile(fileName);
            if (fileContainer.TryGetValue(entityId, out EntityContainer entityContainer)) return entityContainer;
            entityContainer = new EntityContainer(StringComparer.OrdinalIgnoreCase);

            fileContainer[entityId] = entityContainer;
            return entityContainer;
        }

        public EntityContainer GetEntityContainer(string fileName, string entityId) => GetOrCreateEntity(fileName, entityId);

        public void Clear(){
            Files.Clear();
            _refCounts.Clear();
        }

        /// Decrements the reference count for a file and only removes it from memory if the reference count reaches 0.
        public bool ReleaseFile(string fileName){
            if (string.IsNullOrEmpty(fileName)) return false;

            if (_refCounts.TryGetValue(fileName, out int count)){
                count--;
                if (count > 0){
                    _refCounts[fileName] = count;
                    return false;
                }

                _refCounts.Remove(fileName);
            }

            return Files.Remove(fileName);
        }

        /// Decrements the reference count for a file. When the reference count reaches 0, serializes the file to the active session folder and removes it from memory.
        public async Task<bool> ReleaseFileAsync(string fileName, Action<string> onFailure = null){
            if (string.IsNullOrEmpty(fileName)) return false;

            if (_refCounts.TryGetValue(fileName, out int count)){
                count--;
                if (count > 0){
                    _refCounts[fileName] = count;
                    return false;
                }

                _refCounts.Remove(fileName);
            }

            if (!Files.ContainsKey(fileName)) return false;

            await SerializeFile(fileName, onFailure);
            Files.Remove(fileName);
            return true;
        }

        /// Decrements reference counts for multiple files, serializing and removing only those whose reference count reaches 0.
        public async Task ReleaseFilesAsync(IEnumerable<string> fileNames, Action<string> onFailure = null){
            if (fileNames == null) return;

            List<string> filesToFlushAndRemove = new();

            foreach (string fileName in fileNames){
                if (string.IsNullOrEmpty(fileName)) continue;

                if (_refCounts.TryGetValue(fileName, out int count)){
                    count--;
                    if (count > 0){
                        _refCounts[fileName] = count;
                        continue;
                    }

                    _refCounts.Remove(fileName);
                }

                if (Files.ContainsKey(fileName) && !filesToFlushAndRemove.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    filesToFlushAndRemove.Add(fileName);
            }

            if (filesToFlushAndRemove.Count == 0) return;

            List<Task> serializeTasks = filesToFlushAndRemove.Select(f => SerializeFile(f, onFailure)).ToList();
            await Task.WhenAll(serializeTasks);

            foreach (string fileName in filesToFlushAndRemove)
                Files.Remove(fileName);
        }

        public bool Contains(string fileName) => Files.ContainsKey(fileName);

        private static string GetSessionFilePath(string fileName) => Path.Combine(SaveSystem.ActiveSessionDirectory, $"{fileName}.json");

        public Partition GetPartition(string fileName, string entityId, string componentId = "0"){
            EntityContainer entityContainer = GetOrCreateEntity(fileName, entityId);
            if (!entityContainer.TryGetValue(componentId, out Partition partition)){
                partition                    = new Partition(StringComparer.OrdinalIgnoreCase);
                entityContainer[componentId] = partition;
            }

            return partition;
        }

        /// Synchronously acquires a reference to a file, reading from the active session directory only if not already loaded in memory.
        public void AcquireFileSync(string fileName, Action<string> onFailure = null){
            if (string.IsNullOrEmpty(fileName)) return;

            _refCounts[fileName] = GetRefCount(fileName) + 1;

            if (Files.ContainsKey(fileName)) return;

            FileContainer data = ReadFileFromDisk(fileName, onFailure);
            if (data != null)
                Files[fileName] = data;
        }

        /// Acquires references and deserializes multiple save files in parallel on background worker threads from the active session folder.
        /// Files already loaded in memory have their reference count incremented without re-reading disk or overwriting live state.
        public Task AcquireFilesAsync(IEnumerable<string> fileNames, Action<string> onFailure = null) =>
            DeserializeFiles(fileNames, onFailure);

        /// Serializes a single file partition directly into the active session working folder on disk.
        public async Task SerializeFile(string fileName, Action<string> onFailure = null){
            if (!Files.TryGetValue(fileName, out FileContainer data)) return;

            string filePath = GetSessionFilePath(fileName);
            await Task.Run(() => {
                try{
                    string json;
                    lock (data){
                        json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    }

                    File.WriteAllText(filePath, json);
                }
                catch (Exception e){
                    onFailure?.Invoke($"Failed writing session file {fileName}: {e.Message}");
                }
            });
        }

        /// Serializes all active file partitions in parallel into the active session working folder.
        public async Task SerializeBoard(Action<string> onFailure = null){
            List<Task> tasks = Files.Select(board => Task.Run(() => {
                (string fileName, FileContainer data) = board;
                string filePath = GetSessionFilePath(fileName);

                try{
                    string json;
                    lock (data){
                        json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    }

                    File.WriteAllText(filePath, json);
                }
                catch (Exception e){
                    onFailure?.Invoke($"Failed writing session file {fileName} asynchronously: {e.Message}");
                }
            })).ToList();

            await Task.WhenAll(tasks);
        }

        /// Deserializes multiple save files in parallel on background worker threads from the active session folder.
        /// Increments the reference count for each requested file and skips disk reads for files already loaded in RAM.
        public async Task DeserializeFiles(IEnumerable<string> fileNames, Action<string> onFailure = null){
            if (fileNames == null) return;

            List<string> filesToLoad = new();
            foreach (string fileName in fileNames){
                if (string.IsNullOrEmpty(fileName)) continue;

                _refCounts[fileName] = GetRefCount(fileName) + 1;

                if (!Files.ContainsKey(fileName) && !filesToLoad.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    filesToLoad.Add(fileName);
            }

            if (filesToLoad.Count == 0) return;

            List<Task<(string Name, FileContainer Data)>> tasks = filesToLoad.Select(fileName => Task.Run(() => {
                FileContainer diskData = ReadFileFromDisk(fileName, onFailure);
                return (fileName, diskData);
            })).ToList();

            (string Name, FileContainer Data)[] results = await Task.WhenAll(tasks);

            foreach ((string fileName, FileContainer data) in results)
                if (data != null && !Files.ContainsKey(fileName))
                    Files[fileName] = data;
        }

        private static FileContainer ReadFileFromDisk(string fileName, Action<string> onFailure = null){
            if (string.IsNullOrEmpty(SaveSystem.ActiveSessionDirectory))
                return new FileContainer(StringComparer.OrdinalIgnoreCase);

            string filePath = GetSessionFilePath(fileName);

            if (!File.Exists(filePath)){
                try{
                    string dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(filePath, "{}");
                }
                catch (Exception ex){
                    onFailure?.Invoke($"Failed to create empty file {fileName}: {ex.Message}");
                }

                return new FileContainer(StringComparer.OrdinalIgnoreCase);
            }

            try{
                string        json     = File.ReadAllText(filePath);
                FileContainer diskData = JsonConvert.DeserializeObject<FileContainer>(json, new SafeNumericConverter());
                return diskData ?? new FileContainer(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e){
                onFailure?.Invoke($"Session file {fileName} is corrupted: {e.Message}");
                return null;
            }
        }

        public class SafeNumericConverter : JsonConverter{
            public override bool CanConvert(Type objectType) =>
                objectType == typeof(object) ||
                objectType == typeof(Partition) ||
                objectType == typeof(EntityContainer) ||
                objectType == typeof(FileContainer);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer){
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

            private FileContainer ConvertFileContainer(JObject fileObj){
                FileContainer fileContainer = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, JToken> entityProp in fileObj){
                    if (entityProp.Value is JObject entityObj)
                        fileContainer[entityProp.Key] = ConvertEntityContainer(entityObj);
                }

                return fileContainer;
            }

            private EntityContainer ConvertEntityContainer(JObject entityObj){
                EntityContainer entityContainer = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, JToken> compProp in entityObj){
                    if (compProp.Value is JObject compObj){
                        entityContainer[compProp.Key] = ConvertObject(compObj);
                    }
                    else{
                        if (!entityContainer.TryGetValue("0", out Partition defaultPartition)){
                            defaultPartition     = new Partition(StringComparer.OrdinalIgnoreCase);
                            entityContainer["0"] = defaultPartition;
                        }

                        defaultPartition[compProp.Key] = ReadToken(compProp.Value);
                    }
                }

                return entityContainer;
            }

            private object ReadToken(JToken token){
                return token.Type switch{
                    JTokenType.Object => ConvertObject((JObject)token),
                    JTokenType.Array  => ConvertArray((JArray)token),
                    JTokenType.Null   => null,
                    _                 => ParsePrimitiveValue()
                };

                object ParsePrimitiveValue(){
                    object value = token.ToObject<object>();
                    if (value == null) return null;

                    return token.Type switch{
                        JTokenType.Integer => Convert.ToInt32(value),
                        JTokenType.Float   => Convert.ToSingle(value),
                        JTokenType.Boolean => (bool)value,
                        JTokenType.String  => (string)value,
                        _                  => value
                    };
                }
            }

            private Partition ConvertObject(JObject obj){
                Partition dict = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, JToken> prop in obj)
                    dict[prop.Key] = ReadToken(prop.Value);
                return dict;
            }

            private object ConvertArray(JArray arr) => arr.Select(ReadToken).ToList();

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotImplementedException();
            public override bool CanWrite => false;
        }
    }
}
