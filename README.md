# ReactiveBinding

[中文文档](README_CN.md) | English

A compile-time reactive data binding system using C# Source Generator.

## Overview

ReactiveBinding provides attribute-based reactive data binding that generates change detection code at compile time. This eliminates the need for manual change detection logic while avoiding runtime reflection overhead.

## QQ Group：949482664

## Installation

### Unity (UPM)

Unity Package Manager > Add package from git URL:

```
https://github.com/XuToWei/ReactiveBinding.git?path=Unity
```

### .NET (NuGet)

For non-Unity .NET projects (or Unity via [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)):

```bash
dotnet add package XuToWei.ReactiveBinding
```

The NuGet package bundles both the runtime types and the source generator, so no extra setup is needed.

## Quick Start

```csharp
using ReactiveBinding;

public partial class PlayerUI : IReactiveObserver
{
    private PlayerData playerData;

    // Property source
    [ReactiveSource]
    private int Health => playerData.Health;

    // Method source with complex calculation
    [ReactiveSource]
    private int GetTotalDamage() => playerData.BaseDamage + playerData.BonusDamage * playerData.DamageMultiplier;

    // Single source binding
    [ReactiveBind(nameof(Health))]
    private void OnHealthChanged(int oldValue, int newValue)
    {
        healthBar.SetValue(newValue);
    }

    // Multi-source binding - triggered when ANY source changes
    [ReactiveBind(nameof(Health), nameof(GetTotalDamage))]
    private void OnStatsChanged(int newHealth, int newDamage)
    {
        statsText.text = $"HP: {newHealth} DMG: {newDamage}";
    }

    // Auto-inference binding - automatically detects referenced sources
    [ReactiveBind]
    private void OnCombatStatsChanged()
    {
        // Automatically binds to Health and GetTotalDamage
        var ratio = Health / (float)GetTotalDamage();
        combatRating.SetValue(ratio);
    }
}

// Usage
void Update()
{
    playerUI.ObserveChanges();
}
```

Generated code:

```csharp
partial class PlayerUI
{
    private bool __reactive_initialized;
    private int __reactive_Health;
    private int __reactive_GetTotalDamage;

    public void ObserveChanges()
    {
        if (!__reactive_initialized)
        {
            __reactive_initialized = true;
            __reactive_Health = Health;
            __reactive_GetTotalDamage = GetTotalDamage();
            OnHealthChanged(default, Health);
            OnStatsChanged(Health, GetTotalDamage());
            OnCombatStatsChanged();  // Auto-inferred binding
            return;
        }

        bool __changed_Health = false;
        bool __changed_GetTotalDamage = false;
        int __old_Health = __reactive_Health;
        int __old_GetTotalDamage = __reactive_GetTotalDamage;

        if (Health != __reactive_Health)
        {
            __changed_Health = true;
            __reactive_Health = Health;
            OnHealthChanged(__old_Health, Health);
        }

        int __current_GetTotalDamage = GetTotalDamage();
        if (__current_GetTotalDamage != __reactive_GetTotalDamage)
        {
            __changed_GetTotalDamage = true;
            __reactive_GetTotalDamage = __current_GetTotalDamage;
        }

        if (__changed_Health || __changed_GetTotalDamage)
        {
            OnStatsChanged(__reactive_Health, __reactive_GetTotalDamage);
            OnCombatStatsChanged();  // Auto-inferred binding
        }
    }

    public void ResetChanges()
    {
        __reactive_initialized = false;
    }
}
```

## Features

- **Compile-time code generation** - Zero runtime reflection overhead
- **Multiple source types** - Fields, properties, and methods
- **Flexible callbacks** - 0, N, or 2N parameters
- **Multi-source binding** - Bind multiple sources to one callback
- **Auto-inference binding** - Automatically detect referenced sources from method body
- **First-call initialization** - Automatic initial callback trigger
- **Reset support** - `ResetChanges()` for object pooling/reuse
- **Inheritance support** - Derived classes can add their own reactive members with automatic base chaining
- **Throttling** - Control observation frequency
- **Version containers** - VersionList, VersionDictionary, VersionHashSet with efficient version-based change detection
- **VersionField auto-generation** - Auto-generate properties from private fields with version tracking and parent chain propagation
- **Custom property attributes** - `[VersionFieldProperty]` adds custom attributes to generated properties (supports both `Type` and `string`)
- **Data synchronization** - declare a class `: IVersionSync` to sync every `[VersionField]`; a `SyncContext` flat registry serializes into a caller-owned `BinaryWriter` — a full snapshot (`CaptureFull`) or coalesced incremental deltas (`CaptureDelta`)
- **Full diagnostics** - 34 compile-time error/warning codes

## AI-Friendly

Designed for AI-assisted development (Claude, Cursor, GitHub Copilot, etc.):

| Traditional Approach | With ReactiveBinding + AI |
|---------------------|---------------------------|
| Manually write change detection | Declare `[ReactiveSource]` and `[ReactiveBind]`, done |
| Maintain `OnXxxChanged` → `UpdateYyy` → `RefreshZzz` chains | Automatic triggering, zero maintenance |
| Debug by tracing complex call stacks | Just verify binding data is correct, AI infers the rest |
| Forget to unsubscribe events, causing memory leaks | No subscription management, just poll `ObserveChanges()` |
| Scattered update logic across multiple files | All bindings visible in one class with attributes |

**Why AI + ReactiveBinding works so well:**

1. **What you see is what you get** - Generated `.g.cs` files are plain C#, AI can read and reason about them directly
2. **Fail fast** - 31 compile-time diagnostics catch errors before runtime, AI gets immediate feedback
3. **Minimal context needed** - AI only needs to understand "data source → callback", no framework internals
4. **Self-documenting** - Attributes clearly express intent: "when X changes, call Y"

## Attributes

### ReactiveSourceAttribute

Marks a field, property, or method as a reactive data source.

```csharp
[ReactiveSource]
public int Health;              // Field

[ReactiveSource]
public int Mana => _mana;       // Property

[ReactiveSource]
private int GetLevel() => _level;  // Method (must have return value, no parameters)
```

### ReactiveBindAttribute

Marks a method as a callback for data changes. Use `nameof()` to specify sources.

**Callback signatures:**
- `void Method()` - No parameters
- `void Method(T newValue)` - New value only (single source)
- `void Method(T1 new1, T2 new2)` - New values only (multi-source)
- `void Method(T old, T new)` - Old and new values (single source)
- `void Method(T1 old1, T1 new1, T2 old2, T2 new2)` - Old and new pairs (multi-source)

```csharp
// Single source, old and new values
[ReactiveBind(nameof(Health))]
private void OnHealthChanged(int oldValue, int newValue) { }

// Multi-source, no parameters
[ReactiveBind(nameof(Health), nameof(Mana))]
private void OnStatsChanged() { }

// Multi-source, new values only
[ReactiveBind(nameof(Health), nameof(Mana))]
private void OnStatsChangedNew(int newHealth, int newMana) { }
```

#### Auto-Inference Mode

When `[ReactiveBind]` is used without parameters, the generator automatically analyzes the method body to find referenced `[ReactiveSource]` members:

```csharp
[ReactiveSource]
private int Health => playerData.Health;

[ReactiveSource]
private int Mana => playerData.Mana;

// Auto-infer: detects Health and Mana references in method body
[ReactiveBind]
private void OnStatsChanged()
{
    var total = Health + Mana;  // Both are auto-bound
    UpdateUI(total);
}
```

**Notes:**
- Auto-inferred methods must have **no parameters**
- Supports: direct access (`Health`), this access (`this.Health`), method calls (`GetDamage()`)
- Local variable shadowing is handled correctly

### ReactiveThrottleAttribute

Controls how often `ObserveChanges()` actually performs checks.

```csharp
[ReactiveThrottle(10)]  // Only check every 10th call
public partial class PlayerUI : IReactiveObserver
{
    // ...
}
```

### ReactiveObserveIgnoreAttribute

Ignores the RB0003 error when `ObserveChanges()` is not called within the class. Use this when `ObserveChanges()` is called externally (e.g., by a manager or framework).

```csharp
[ReactiveObserveIgnore]
public partial class PlayerUI : IReactiveObserver
{
    // ObserveChanges() is called by an external manager, not within this class
}
```

## VersionField Auto-Generation

Use `[VersionField]` to automatically generate properties from private fields with change tracking. When the property value changes, the version is incremented and propagated up through the parent chain.

### Basic Usage

```csharp
public partial class PlayerData : IVersion
{
    [VersionField] private int __Health;
    [VersionField] private float __Speed;
    [VersionField] private string __Name;
}
```

The generated property name strips the `__` prefix and capitalizes the first letter (`__Health` → `Health`, `__playerName` → `PlayerName`).

### Generated Code

```csharp
partial class PlayerData
{
    public ReactiveBinding.IVersion __Parent { get; set; }
    public int __Version { get; private set; }

    public void __IncrementVersion()
    {
        __Version = ReactiveBinding.VersionCounter.Next();
        if (__Parent != null) __Parent.__IncrementVersion();
    }

    public int Health
    {
        get => __Health;
        set
        {
            if (value != __Health)
            {
                __Health = value;
                __IncrementVersion();
            }
        }
    }

    public float Speed
    {
        get => __Speed;
        set
        {
            if (System.Math.Abs(value - __Speed) > 1e-6f)
            {
                __Speed = value;
                __IncrementVersion();
            }
        }
    }
    // ...
}
```

### Custom Property Attributes

Use `[VersionFieldProperty]` to add custom attributes to generated properties. Supports two constructors:

- `VersionFieldProperty(Type type)` — for parameterless attributes, automatically resolves the full namespace
- `VersionFieldProperty(string text)` — for attributes with parameters, outputs the text verbatim

```csharp
public partial class PlayerData : IVersion
{
    [VersionField]
    [VersionFieldProperty(typeof(JsonIgnoreAttribute))]
    private int __Health;

    [VersionField]
    [VersionFieldProperty("System.Obsolete(\"Use NewName\")")]
    private string __Name;

    [VersionField]
    [VersionFieldProperty(typeof(JsonIgnoreAttribute))]
    [VersionFieldProperty("System.Obsolete(\"Use NewSpeed\")")]
    private float __Speed;
}
```

Generated:

```csharp
[Newtonsoft.Json.JsonIgnoreAttribute]
public int Health { get => __Health; set { ... } }

[System.Obsolete("Use NewName")]
public string Name { get => __Name; set { ... } }

[Newtonsoft.Json.JsonIgnoreAttribute]
[System.Obsolete("Use NewSpeed")]
public float Speed { get => __Speed; set { ... } }
```

### Nested IVersion Fields

When a field type implements `IVersion`, the generator automatically manages the parent chain:

```csharp
public partial class GameData : IVersion
{
    [VersionField] private PlayerData __Player;  // PlayerData : IVersion
}

// Generated setter:
public PlayerData Player
{
    get => __Player;
    set
    {
        if (value != __Player)
        {
            if (__Player != null) __Player.__Parent = null;  // Clear old parent
            __Player = value;
            if (value != null) value.__Parent = this;        // Set new parent
            __IncrementVersion();
        }
    }
}
```

### Version Propagation

Version changes propagate up through the entire parent chain:

```
GameData (__Parent=null)
  └── PlayerData (__Parent=GameData)
        └── WeaponData (__Parent=PlayerData)

When WeaponData.Damage changes:
  → WeaponData.Version changes
  → PlayerData.Version changes
  → GameData.Version changes
```

### Container Fields

Version containers can also be used as fields with automatic parent chain management:

```csharp
public partial class InventoryData : IVersion
{
    [VersionField] private VersionList<ItemData> __Items;
    [VersionField] private int __Gold;
}

public partial class TeamData : IVersion
{
    [VersionField] private VersionDictionary<string, PlayerData> __Players;
}
```

### Complex Hierarchy Example

A complete example with 3-level nesting and containers:

```csharp
// Level 3 - Leaf
public partial class SkillData : IVersion
{
    [VersionField] private int __Damage;
    [VersionField] private float __CoolDown;
}

// Level 2 - Middle (with container)
public partial class CharacterData : IVersion
{
    [VersionField] private int __Health;
    [VersionField] private VersionList<SkillData> __Skills;
}

// Level 1 - Root (with both single and container)
public partial class GameData : IVersion
{
    [VersionField] private CharacterData __MainCharacter;
    [VersionField] private VersionList<CharacterData> __AllCharacters;
}

// Usage:
var game = new GameData();
var player = new CharacterData();
var skill = new SkillData();

game.MainCharacter = player;        // player.__Parent = game
player.Skills.Add(skill);           // skill.__Parent = player.Skills, Skills.__Parent = player

skill.Damage = 100;                 // All versions change:
                                    // skill.Version ↑
                                    // player.Skills.Version ↑
                                    // player.Version ↑
                                    // game.Version ↑
```

### Requirements

1. Class must be `partial`
2. Class must implement `IVersion`
3. Fields must have `__` prefix
4. Fields must be `private`

## Data Synchronization

Declare a `[VersionField]` class as `: IVersionSync` to make the object tree synchronizable. Sync is opt-in **at the class level** — every `[VersionField]` in an `IVersionSync` class is synced (there is no per-field attribute); a class declared `: IVersion` gets version tracking only. Synchronization is a **flat registry + full snapshot, with optional coalesced deltas**: a `SyncContext` holds every syncable node in a `Dictionary<int, node>` keyed by a stable id. The **caller owns the stream** — `CaptureFull(writer)` writes the whole registry into a `BinaryWriter` as a complete, self-contained snapshot (keyframe); after a baseline, `CaptureDelta(writer)` writes only the nodes that changed since the last capture. `Apply(reader)` rebuilds the consumer to match (a full snapshot also drops any node it didn't mention).

```csharp
public partial class PlayerData : IVersionSync   // all [VersionField] below are synced
{
    [VersionField] private int __Health;
    [VersionField] private string __Name;
}
```

### SyncContext

`SyncContext` is a thin registry kernel — exposed state plus three operations; seed the root with `root.AttachTo(ctx)`:

```csharp
public class SyncContext
{
    public readonly Dictionary<int, IVersionSync> __Objects;  // registry: id -> node (driven inline by generated code)
    public int __NextId;                                      // id allocator (root gets 1)

    public void CaptureFull(BinaryWriter w);   // write the whole registry as a full snapshot (keyframe), clear dirty
    public void CaptureDelta(BinaryWriter w);  // write only the nodes changed since the last capture (incremental)
    public void Apply(BinaryReader r);         // apply a snapshot/delta from the reader's position to EOF
}
```

### Usage

```csharp
// Producer: create a context and seed the root
var producerCtx = new SyncContext();
var producer = new PlayerData();
producer.AttachTo(producerCtx);
producer.Health = 100;

// CaptureFull writes the whole registry into a caller-owned writer as a full snapshot
var ms = new MemoryStream();
producerCtx.CaptureFull(new BinaryWriter(ms));
byte[] payload = ms.ToArray();   // normally you'd ship these bytes over a transport

// Consumer: seed the SAME root (both sides assign it id 1), then apply
var consumerCtx = new SyncContext();
var consumer = new PlayerData();
consumer.AttachTo(consumerCtx);
consumerCtx.Apply(new BinaryReader(new MemoryStream(payload)));

// Later: mutate, then ship an incremental delta (only changed nodes) onto the existing consumer state
producer.Health = 80;
var delta = new MemoryStream();
producerCtx.CaptureDelta(new BinaryWriter(delta));
consumerCtx.Apply(new BinaryReader(new MemoryStream(delta.ToArray())));
```

`Apply` mutates silently (it never writes back), updates existing nodes in place (object identity preserved), and creates referenced nodes on first sight. `CaptureFull` writes the complete state — a self-contained keyframe that, on apply, also prunes any node it didn't mention; `CaptureDelta` writes only the nodes changed since the last capture, applied in place with no prune (ship a periodic `CaptureFull` to drop removed nodes).

### Model

- **Flat registry, snapshot + deltas.** Each node has a stable `__SyncId`. Both capture methods scan the registry by ascending id (parent < descendants) and write a `[byte isFull]` marker followed by a flat list of node records read until EOF — `[id][payload]` per node (an object node's payload is `[mask][changed-field values]`; a container's is `[full-byte]` then its full contents or an op log). `CaptureFull` writes every node (isFull=1, a complete keyframe; on apply, nodes it didn't mention are pruned); `CaptureDelta` writes only dirty nodes (isFull=0, applied in place, no prune).
- **References, not recursion.** An object/container field serializes as the referenced node's `__SyncId` (0 = null). The consumer creates a node the first time a reference to it is read (inline in the node's `__Apply`, via `ctx.__Objects`) using the field's **static** type — no type tags on the wire. Node ids are assigned pre-order (parent < descendants), so a parent's reference record is always read before the referenced node's own records.
- **Apply rebuilds to match.** Existing nodes update in place (object identity preserved — good for bindings); referenced nodes are created on first sight; and because the marker says full, any registered node the snapshot didn't mention is dropped afterward (it was removed on the producer). `Apply` never writes back.
- **Collections.** A synced `[VersionField]` container must be a `VersionSyncList`/`VersionSyncDictionary`/`VersionSyncHashSet` (the version-only `VersionList`/etc. are not syncable → VS2001). They are registry nodes serialized as their full contents (or a coalesced per-frame op log in a delta). In a `VersionSyncList` of `IVersionSync` objects, each element is its own registry node referenced by id, and syncs its own fields independently.

### Supported field types

- Scalars: `bool/byte/sbyte/short/ushort/int/uint/long/ulong/float/double/char/decimal/string/enum`
- A nested **concrete** `IVersionSync` type
- `VersionSyncList<T>` where `T` is a scalar or a concrete `IVersionSync` type (object elements sync as their own nodes)
- `VersionSyncDictionary<K,V>` and `VersionSyncHashSet<T>` with scalar key/value/element types

### Limitations

- Up to 64 synced `[VersionField]` members per class.
- Both sides must seed the same root via `root.AttachTo(ctx)` before the first `Apply` (both deterministically assign it id 1).
- `SyncObject`/container members must be concrete types instantiable with `new T()`; interfaces/abstract/polymorphic are not supported.
- `VersionSyncDictionary` object **values** and `VersionSyncHashSet` object **elements** are not supported (VS2001) — keys/values/elements must be scalar.

## Version Containers

ReactiveBinding provides version-based containers for efficient collection change detection. Instead of comparing collection contents, only the version number is compared.

### Available Containers

- `VersionList<T>` - Implements `IList<T>, IVersion`
- `VersionDictionary<K,V>` - Implements `IDictionary<K,V>, IVersion`
- `VersionHashSet<T>` - Implements `ISet<T>, IVersion`

Each modification (Add, Remove, Clear, etc.) increments the `__Version` property.

### Usage Example

```csharp
public partial class InventoryUI : MonoBehaviour, IReactiveObserver
{
    [ReactiveSource]
    private VersionList<Item> Items = new();

    // No parameters - just notified of change
    [ReactiveBind(nameof(Items))]
    private void OnItemsChanged()
    {
        RefreshUI();
    }

    // With container parameter - receives the container
    [ReactiveBind(nameof(Items))]
    private void OnItemsChangedWithParam(VersionList<Item> items)
    {
        Debug.Log($"Items count: {items.Count}");
    }

    void Update() => ObserveChanges();
}
```

### Generated Code

```csharp
partial class InventoryUI
{
    private bool __reactive_initialized;
    private int __reactive_Items_version = -1;  // Stores version, not content

    public void ObserveChanges()
    {
        if (!__reactive_initialized)
        {
            __reactive_initialized = true;
            __reactive_Items_version = Items?.Version ?? -1;
            OnItemsChanged();
            OnItemsChangedWithParam(Items);
            return;
        }

        var __current_Items_version = Items?.Version ?? -1;
        if (__current_Items_version != __reactive_Items_version)
        {
            __reactive_Items_version = __current_Items_version;
            OnItemsChanged();
            OnItemsChangedWithParam(Items);
        }
    }

    public void ResetChanges()
    {
        __reactive_initialized = false;
    }
}
```

### Callback Signatures for Version Containers

- `void Method()` - No parameters
- `void Method(ContainerType container)` - Receives the container itself

### Mixed Version Containers and Basic Types

Version containers can be combined with basic types in multi-source bindings:

```csharp
[ReactiveSource]
private VersionList<Item> Items = new();

[ReactiveSource]
private int TotalCount;

// Mixed binding - version container gets container, basic type gets newValue
[ReactiveBind(nameof(Items), nameof(TotalCount))]
private void OnDataChanged(VersionList<Item> items, int count)
{
    Debug.Log($"Items: {items.Count}, Total: {count}");
}
```

> Note: When mixing version containers with basic types, 2N parameters (old/new pairs) are not supported since version containers cannot track previous state.

## Inheritance

Derived classes can add their own reactive members. Each class handles its own `[ReactiveSource]` and `[ReactiveBind]`, and the generated code chains automatically via `base.ObserveChanges()`.

```csharp
public partial class BaseUI : MonoBehaviour, IReactiveObserver
{
    [ReactiveSource]
    protected int Health => data.Health;

    [ReactiveBind(nameof(Health))]
    private void OnHealthChanged(int oldValue, int newValue) { }
}

public partial class DerivedUI : BaseUI
{
    [ReactiveSource]
    private int Mana => data.Mana;

    [ReactiveBind(nameof(Mana))]
    private void OnManaChanged(int newValue) { }
}
```

Generated for `DerivedUI`:

```csharp
partial class DerivedUI
{
    private bool __reactive_initialized;
    private int __reactive_Mana = default!;

    public override void ObserveChanges()
    {
        base.ObserveChanges();  // Handles Health change detection

        if (!__reactive_initialized)
        {
            __reactive_initialized = true;
            __reactive_Mana = Mana;
            OnManaChanged(Mana);
            return;
        }
        // Mana change detection...
    }

    public override void ResetChanges()
    {
        base.ResetChanges();
        __reactive_initialized = false;
    }
}
```

- Only `[ReactiveBind]` triggers code generation for derived classes; `[ReactiveSource]` alone does not
- `virtual` is only added when a derived class in the same compilation has `[ReactiveBind]` and needs to `override`
- Derived classes without `[ReactiveBind]` skip generation entirely (inherit from base)
- Each class only handles its own `[ReactiveSource]` and `[ReactiveBind]` members
- Manual `ObserveChanges()`/`ResetChanges()` is forbidden in all `IReactiveObserver` classes (RB1005/RB1006)

## IReactiveObserver Interface

Classes using `[ReactiveBind]` must implement `IReactiveObserver`. The Source Generator automatically implements `ObserveChanges()` and `ResetChanges()`.

```csharp
public interface IReactiveObserver
{
    void ObserveChanges();
    void ResetChanges();
}
```

- `ObserveChanges()` - Check for data changes and trigger bound callbacks. On first call (or after reset), all callbacks are triggered with default as oldValue.
- `ResetChanges()` - Reset the reactive state so the next `ObserveChanges()` call behaves as the first call. Useful for object pooling/reuse scenarios.

## Requirements

1. Class must be `partial`
2. Class must implement `IReactiveObserver`
3. `[ReactiveBind]` with explicit sources must use `nameof()` expressions (or use auto-inference without parameters)
4. `[ReactiveSource]` methods must have return values and no parameters
5. `[ReactiveSource]` properties must have getters
6. Custom struct types must implement `==` and `!=` operators

## Compiler Diagnostics

| Code | Type | Description |
|------|------|-------------|
| RB0001 | Warning | ReactiveSource has no corresponding ReactiveBind |
| RB0002 | Error | ReactiveBind references non-existent source |
| RB0003 | Error | ObserveChanges() not called in class, use [ReactiveObserveIgnore] to ignore |
| RB1001 | Error | Class must be partial |
| RB1002 | Error | Class must implement IReactiveObserver |
| RB1003 | Error | ReactiveThrottle value must be >= 1 |
| RB1004 | Error | ReactiveThrottle without IReactiveObserver |
| RB1005 | Error | Manual ObserveChanges() implementation not allowed |
| RB1006 | Error | Manual ResetChanges() implementation not allowed |
| RB2001 | Error | ReactiveSource method returns void |
| RB2002 | Error | ReactiveSource property has no getter |
| RB2003 | Error | ReactiveSource method has parameters |
| RB2004 | Error | Unsupported ReactiveSource type |
| RB2005 | Error | Struct missing equality operator |
| RB3001 | Error | ReactiveBind has no identities |
| RB3002 | Error | ReactiveBind method is static |
| RB3003 | Error | ReactiveBind method doesn't return void |
| RB3004 | Error | Invalid parameter count |
| RB3005 | Error | Parameter type mismatch |
| RB3006 | Error | Duplicate identities |
| RB3007 | Error | Not using nameof() |
| RB3008 | Error | Auto-inference found no sources in method body |
| RB3009 | Error | Auto-inferred method cannot have parameters |
| RB3010 | Error | Referenced member exists but not marked with [ReactiveSource] |
| VF1001 | Error | VersionField class must be partial |
| VF1002 | Error | VersionField class must implement IVersion |
| VF2001 | Error | VersionField must have __ prefix |
| VF2002 | Error | VersionField must be private |
| VF2003 | Error | Property name already exists |
| VF3001 | Error | __Parent property access not allowed outside IVersion |
| VF3002 | Error | Direct access to VersionField backing field not allowed |
| VF3003 | Error | VersionField must not have a default value initializer |
| VS2001 | Error | Unsupported synced field type (a [VersionField] in an IVersionSync class) |
