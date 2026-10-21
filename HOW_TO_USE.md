# BlackboardSaveSystem - Integration & Usage Guide

This guide provides step-by-step instructions for integrating and using the `BlackboardSaveSystem` in your Unity project.

---

## 1. Installation

### Option A: Unity Package Manager (UPM via Git URL)
1. Open your Unity Project.
2. Navigate to **Window -> Package Manager**.
3. Click the **+** button in the top left and select **Add package from git URL...**.
4. Enter the repository URL:
   ```
   https://github.com/ThiagoDAraujoS/BlackboardSaveSystem.git
   ```

### Option B: Git Submodule
If you prefer adding the repository as a Git submodule directly in your Unity project's assets folder:
```bash
git submodule add https://github.com/ThiagoDAraujoS/BlackboardSaveSystem.git Assets/ImportedToolsAndLibraries/BlackboardSaveSystem
```

---

## 2. Manager Setup

1. Create a persistent manager `GameObject` in your initial boot scene (e.g., `[SaveSystem]`).
2. Attach the `SaveSystem` component (which automatically adds `Blackboard`).
3. Set `coreFileNames` in the Inspector to contain your global save files (e.g., `"core"`).

```csharp
using BlackboardSaveSystem;
using UnityEngine;

public class GameInitializer : MonoBehaviour {
    private void Start() {
        // Initialize an active save session copying from a slot
        SaveSystem.InitializeSession("Slot_1");
    }
}
```

---

## 3. Configuring Scene Save Scopes (`BlackboardScope`)

`BlackboardScope` automates file acquisition and release using Unity's component lifecycle (`OnEnable` / `OnDisable`).

1. Add a `BlackboardScope` component to a room, area root, or manager `GameObject`.
2. In the Inspector, populate the `fileNames` list with the save files required by entities under this scope (e.g., `"dungeon_level_01"`).

When the `GameObject` is enabled (or scene loaded), `BlackboardScope` acts as an RAII smart pointer that increments reference counts on its target `BlackboardFile` instances, loading them from disk into memory. When `BlackboardClient` components enable afterwards, they look up their `BlackboardFile` directly on the `Blackboard` and subscribe to its `OnUnload` event. When the last scope holding a reference to a file is disabled (dropping `RefCount` to `0`), the `BlackboardFile` invokes `OnUnload` so subscribed clients flush their state before the file is removed from RAM and serialized to disk.

---

## 4. Entity State Binding (`BlackboardClient` & `UniqueId`)

To make a `GameObject` stateful and persistent:

1. Attach a `UniqueId` component to the `GameObject`. A unique GUID is generated automatically in the Unity Editor.
2. Attach a `BlackboardClient` component to the same `GameObject`.
3. Set the `fileName` field on `BlackboardClient` to match the file managed by the active `BlackboardScope` (e.g., `"dungeon_level_01"`).

---

## 5. Creating Custom State Components (`TrackedBehaviour` & `Tracked<T>`)

To track custom gameplay variables on an entity without memory allocation during gameplay:

1. Create a script inheriting from `TrackedBehaviour`.
2. Declare `Tracked<T>` fields passing a unique string key and default value in constructor initializers.

```csharp
using BlackboardSaveSystem;
using UnityEngine;

public class PlayerStats : TrackedBehaviour {
    // Zero-allocation gameplay state decorators
    public Tracked<int> Health = new("health", 100);
    public Tracked<int> MaxHealth = new("maxHealth", 100);
    public Tracked<string> PlayerName = new("playerName", "Hero");

    public void TakeDamage(int damage) {
        // Implicit cast allows direct arithmetic and assignment
        Health.Value = Mathf.Max(0, Health - damage);
        Debug.Log($"[PlayerStats] Health is now {Health}");
    }
}
```

### Tracking Position and Rotation (`TrackedTransform`)
Attach `TrackedTransform` to any `GameObject` needing persistent coordinates. It automatically handles `NavMeshAgent` warping when restoring position.

---

## 6. Saving and Session Management

### Saving Active Session State
To commit live entity states and flush RAM to disk:
```csharp
// Commit all loaded files to the _active_session working directory
await SaveSystem.CommitToActiveSessionAsync();
```

### Custom Save Slot Metadata (`MetadataProvider` & `SaveFileMetadata`)
Provide custom UI metadata variables by assigning `SaveSystem.MetadataProvider`:
```csharp
SaveSystem.MetadataProvider = () => new Dictionary<string, string> {
    ["location"] = "Abandoned Outpost",
    ["characterName"] = "Protagonist"
};

// Reading save slots for UI lists (returns "VOID" via Null Object pattern if a key or meta.json is unreadable)
foreach (SaveFileMetadata meta in SaveSystem.GetSaveFileList()) {
    string location = meta.Get("location");
    string character = meta.Get("characterName");
}
```

### Direct Component State Access
Access active partitions directly from `BlackboardClient`:
```csharp
BlackboardClient client = GetComponent<BlackboardClient>();

// Access partition for first component (index "0")
var partition = client.GetPartition(0);
partition["custom_flag"] = true;
```
