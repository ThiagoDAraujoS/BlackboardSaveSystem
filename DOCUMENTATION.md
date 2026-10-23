# BlackboardSaveSystem - Technical Architecture & API Reference

`BlackboardSaveSystem` is a decoupled, high-performance RAII save and state management framework for Unity. It provides isolated state partitioning, reference-counted file lifecycles, and zero-allocation gameplay performance through `Tracked<T>` state decorators.

---

## 1. System Architecture

The save system organizes state using a deterministic, 4-level dictionary hierarchy:

```
fileName -> entityId -> componentId -> variableName -> value
```

### 4-Level Data Hierarchy
1. **FileName** (`string`): Identifies the save file / partition (e.g., `"core"`, `"room_01"`, `"player_inventory"`).
2. **EntityId** (`string`): The persistent GUID assigned to a `GameObject` via `UniqueId`.
3. **ComponentId** (`string`): The zero-based order index (`"0"`, `"1"`, `"2"`) of `IBoundState` components on the entity, preventing key collisions when multiple components on the same entity track variables with identical names.
4. **VariableName** (`string`): The key associated with a specific `Tracked<T>` variable or state entry.
5. **Value** (`object`): The stored primitive, vector array, or JSON-serializable value.

### Serialized JSON Structure
```json
{
  "8f9a2b1c-3d4e-5f6a-7b8c-9d0e1f2a3b4c": {
    "0": {
      "health": 100,
      "maxHealth": 100
    },
    "1": {
      "body_position": [ 12.5, 0.0, -4.2 ],
      "body_rotation": [ 0.0, 90.0, 0.0 ]
    }
  }
}
```

---

## 2. Reference-Counted File Lifecycle & Event Architecture

`Blackboard` manages save files in memory using `BlackboardFile` instances with explicit reference counting (`RefCount`) and a clean event-driven lifecycle:

```
SaveSystem (-1200) -> Blackboard (-1100) -> BlackboardScope (-1000) -> BlackboardClient (0)
```

- **`Blackboard`** holds `Files` (`Dictionary<string, BlackboardFile>`), provides direct $O(1)$ lookup (`GetOrCreateFile`) for clients, and exposes `OnDeactivate` (`event Action`) to force active `BlackboardScope` instances to release if `Blackboard` disables.
- **`BlackboardFile`** holds its `Data` (`FileContainer` dictionary), `RefCount`, `OnUnload` (`event Action`) for `BlackboardClient` instances, and all file unload and serialization procedures.
- **`BlackboardScope`** is a pure RAII smart pointer (`MonoBehaviour`) that anchors configured files in RAM while enabled and has zero awareness of clients.
- **`BlackboardClient`** is a pure leaf data proxy that subscribes directly to its `BlackboardFile.OnUnload` event.

```
                  AcquireFileSync() (RefCount == 0)
                     ├──> Read file from disk into BlackboardFile
                     └──> Set file.RefCount = 1
  ┌───────────────┐
  │ BlackboardFile│  AcquireFileSync() (RefCount > 0)
  │ (In RAM)      │  └──> Increment file.RefCount (No disk I/O)
  └───────┬───────┘
          │          ReleaseFileAsync() (RefCount > 1)
          │          └──> Decrement file.RefCount (Keep in RAM)
          │
          └─────────> ReleaseFileAsync() (RefCount == 1 -> 0)
                     ├──> file.NotifyUnloading() -> invokes OnUnload on subscribed Clients
                     ├──> Remove BlackboardFile from Blackboard.Files
                     └──> file.SerializeAsync() writes JSON to disk
```

- **`AcquireFileSync` / `AcquireFilesAsync`**: Increments the `BlackboardFile.RefCount`. If the file is not currently loaded in memory, it is deserialized from `_active_session/<fileName>.json` into a new `BlackboardFile`. If already loaded, disk read is skipped to preserve live in-memory state.
- **`ReleaseFile` / `ReleaseFileAsync` / `ReleaseFilesAsync`**: Decrements `BlackboardFile.RefCount`. When `RefCount` drops to `0`, `file.NotifyUnloading()` invokes `OnUnload` so subscribed `BlackboardClient` leaf nodes flush their latest state, the file is removed from `Blackboard.Files`, and `file.SerializeAsync()` writes the container to disk (`_active_session/<fileName>.json`).

---

## 3. Blackboard & BlackboardFile API Reference

### Blackboard Fields, Properties & Events

- `public Dictionary<string, BlackboardFile> Files { get; }`
  Returns the active in-memory dictionary of loaded `BlackboardFile` instances.
- `public event Action OnDeactivate`
  Broadcasts deactivation to all subscribed `BlackboardScope` instances when `Blackboard` is disabled.

### BlackboardFile Class

Encapsulates a single save file's in-memory dictionary, reference count, and unload lifecycle events:

```csharp
public class BlackboardFile {
    public readonly string Name;
    public readonly FileContainer Data;
    public int RefCount { get; set; }
    public event Action OnUnload;

    public BlackboardFile(string name, FileContainer data = null);
    public void CollectData();
    public void NotifyUnloading();
    public EntityContainer GetOrCreateEntity(string entityId);
    public Partition GetPartition(string entityId, string componentId = "0");
    public Task SerializeAsync(Action<string> onFailure = null);
    public static FileContainer ReadFromDisk(string fileName, Action<string> onFailure = null);
}
```

### Blackboard Methods

```csharp
public BlackboardFile GetOrCreateFile(string fileName)
```
Retrieves an active `BlackboardFile` or creates a new one if not yet loaded.

```csharp
public void CollectDataForFile(string fileName)
public void CollectAllData()
```
Triggers `CollectData()` (`OnUnload`) on a specific `BlackboardFile` or across all loaded `BlackboardFile` instances.

```csharp
public int GetRefCount(string fileName)
```
Returns the current active reference count (`file.RefCount`) for the specified file.

```csharp
public EntityContainer GetOrCreateEntity(string fileName, string entityId)
```
Returns or creates the `EntityContainer` dictionary for the given entity GUID inside the specified file.

```csharp
public Partition GetPartition(string fileName, string entityId, string componentId = "0")
```
Retrieves or creates the target `Partition` (`Dictionary<string, object>`) for a specific component on an entity.

```csharp
public void AcquireFileSync(string fileName, Action<string> onFailure = null)
```
Synchronously increments the file reference count and reads the file from disk if not present in RAM.

```csharp
public Task AcquireFilesAsync(IEnumerable<string> fileNames, Action<string> onFailure = null)
```
Asynchronously acquires and deserializes multiple save files in parallel on background worker threads.

```csharp
public bool ReleaseFile(string fileName)
```
Synchronously decrements reference count and removes the file from memory if count reaches `0`.

```csharp
public async Task<bool> ReleaseFileAsync(string fileName, Action<string> onFailure = null)
```
Asynchronously decrements reference count, serializes the file to disk if count reaches `0`, and purges it from memory.

```csharp
public async Task ReleaseFilesAsync(IEnumerable<string> fileNames, Action<string> onFailure = null)
```
Asynchronously releases multiple files in parallel, serializing and purging those whose reference count reaches `0`.

```csharp
public async Task SerializeFile(string fileName, Action<string> onFailure = null)
```
Serializes a single file partition to `_active_session/<fileName>.json`.

```csharp
public async Task SerializeBoard(Action<string> onFailure = null)
```
Serializes all currently loaded file partitions in parallel to disk.

```csharp
public void Clear()
```
Clears all in-memory files and resets reference counters.

---

## 4. SaveSystem API Reference

`SaveSystem` coordinates disk directories, save slots, and active game session folders. It requires a `Blackboard` component attached to the same GameObject (`[DefaultExecutionOrder(-1200)]`).

### Properties

- `public static SaveSystem Instance { get; }`
- `public static Blackboard Blackboard { get; }`
- `public static string CurrentSaveSlot { get; }`
- `public static string CurrentSaveSlotDirectory { get; }`
- `public static string ActiveSessionDirectory { get; }`
- `public static string TempDirectory { get; }`

### Delegate Hooks

- `public static Func<Dictionary<string, string>> MetadataProvider`: Delegate provided by host games to return custom key-value save metadata (e.g., `"location"`, `"characterName"`, `"chapter"`).

### SaveFileMetadata (Null Object Pattern)

`SaveFileMetadata` stores slot metadata in a dynamic `Dictionary<string, string> Variables` container and uses the **Null Object Pattern** (`VoidValue = "VOID"`) when metadata files or keys cannot be read:

```csharp
[Serializable]
public struct SaveFileMetadata {
    public const string VoidValue = "VOID";

    public string slotName;
    public DateTime lastSaveTime;
    public Dictionary<string, string> Variables;

    public SaveFileMetadata(string slotName, DateTime lastSaveTime, Dictionary<string, string> variables = null);
    public static SaveFileMetadata CreateVoid(string slotName = VoidValue);
    public string Get(string key);
}
```
- `Get(string key)` returns the stored variable string if present and non-empty, or `"VOID"` (`SaveFileMetadata.VoidValue`) if the key or `meta.json` could not be read.

### Methods

```csharp
public static void SetSaveSlot(string slotName)
```
Sets the current active save slot directory name and ensures the folder exists on disk.

```csharp
public static void InitializeSession(string sourceSlotName)
```
Clears the active session folder (`_active_session`) and copies all files from the specified save slot into `_active_session`.

```csharp
public static async Task CommitToActiveSessionAsync(Action<string> onFailure = null)
```
Flushes all active `BlackboardClient` entities and serializes all loaded memory files to disk in `_active_session`.

```csharp
public static async Task CommitFileAsync(string fileName)
```
Flushes active entities belonging to `fileName` and serializes that specific file to disk.

```csharp
public static void ClearActiveMemory()
```
Purges all loaded data partitions from RAM.

```csharp
public static void AcquireFileSync(string fileName)
public static Task AcquireFilesAsync(IEnumerable<string> fileNames)
public static Task ReleaseFilesAsync(IEnumerable<string> fileNames)
```
Helper proxies forwarding acquisition and release requests to the underlying `Blackboard`.

---

## 5. BlackboardScope API Reference

`BlackboardScope` is a pure RAII `MonoBehaviour` smart pointer (`[DefaultExecutionOrder(-1000)]`) that anchors configured `BlackboardFile` instances in RAM while enabled (`OnEnable` / `OnDisable`) and subscribes to `Blackboard.OnDeactivate`.

### Public Fields

- `public List<string> fileNames`: List of Blackboard file names to keep loaded while this scope is active.

### Methods

```csharp
public void AcquireScope()
```
Acquires reference counts for all listed `fileNames` and subscribes to `Blackboard.OnDeactivate`. Automatically called during `OnEnable()`.

```csharp
public async Task ReleaseScopeAsync()
```
Unsubscribes from `Blackboard.OnDeactivate` and releases reference counts for all acquired files. Automatically called during `OnDisable()`.

---

## 6. BlackboardClient API Reference

`BlackboardClient` (`[DefaultExecutionOrder(0)]`) is a pure leaf-node data proxy between local `IBoundState` components on a `GameObject` and its target `BlackboardFile`. Requires a `UniqueId` component.

### Public Fields & Properties

- `public string fileName`: Target save file name (e.g., `"room_01"`).
- `public string EntityId { get; }`: GUID retrieved from `UniqueId`.

### Methods

```csharp
public void FlushStateToBlackboard()
```
Calls `OnSaveState(partition)` on all local `IBoundState` components using their order index (`"0"`, `"1"`, ...). Automatically subscribed to `BlackboardFile.OnUnload` while the client is enabled.

```csharp
public void LoadStateFromBlackboard()
```
Calls `OnLoadState(partition)` on all local `IBoundState` components using their order index (`"0"`, `"1"`, ...).

```csharp
public Partition GetPartition(int componentIndex = 0)
public Partition GetPartition(string componentId)
```
Retrieves the specific partition dictionary for a given component index from the bound `BlackboardFile`.

---

## 7. TrackedBehaviour & Tracked<T> API Reference

### ITracked Interface
```csharp
public interface ITracked {
    string Key { get; }
    void Save(Dictionary<string, object> state);
    void Load(Dictionary<string, object> state);
}
```

### Tracked<T> Class
`Tracked<T>` provides high-performance local backing state with zero-allocation gameplay access, serializing to `Blackboard` only during save/load operations.

```csharp
public class Tracked<T> : ITracked {
    public T Value { get; set; }
    public Tracked(string key, T defaultValue);
    public static implicit operator T(Tracked<T> tracked);
}
```

### TrackedBehaviour Class
Abstract base class for pure-data components. Uses reflection on `Awake()` to discover all `ITracked` fields and automatically synchronizes them in `OnSaveState` and `OnLoadState`.

```csharp
public abstract class TrackedBehaviour : MonoBehaviour, IBoundState {
    public virtual void OnSaveState(Dictionary<string, object> state);
    public virtual void OnLoadState(Dictionary<string, object> state);
    protected virtual void OnAwake();
}
```

---

## 8. TrackedTransform API Reference

`TrackedTransform` inherits from `TrackedBehaviour` and automatically tracks position (`body_position`) and optionally rotation (`body_rotation`).

```csharp
public class TrackedTransform : TrackedBehaviour {
    [SerializeField] protected Transform targetBody;
    [SerializeField] protected bool saveRotation = true;
}
```
- **NavMeshAgent Integration**: Automatically detects an attached `NavMeshAgent` and uses `agent.Warp(position)` when restoring position.

---

## 9. UniqueId API Reference

`UniqueId` provides permanent, globally unique string identifiers for GameObjects generated in the Unity Editor.

```csharp
public class UniqueId : MonoBehaviour {
    public string Id { get; }
}
```

---

## 10. Assembly Definitions & Execution Order

- **`BlackboardSaveSystem.Runtime.asmdef`**: Target runtime assembly containing all core logic.
- **`BlackboardSaveSystem.Editor.asmdef`**: Unity Editor scripts, custom inspectors, and execution-order enforcers.
- **Execution Order Specification**:
  - `SaveSystem`: `-1200` (Initializes singleton and path mappings)
  - `Blackboard`: `-1100` (Manages file containers, reference counts, and active `Scopes`)
  - `BlackboardScope`: `-1000` (Acquires file leases before entities awaken and collects client data before eviction)
  - `BlackboardClient`: `0` (Leaf node that binds entity components to loaded partitions)
