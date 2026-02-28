# ReactiveBinding

[中文文档](README_CN.md) | English

A compile-time reactive data binding system using C# Source Generator.

## Overview

ReactiveBinding provides attribute-based reactive data binding that generates change detection code at compile time. This eliminates the need for manual change detection logic while avoiding runtime reflection overhead.

## QQ Group：949482664

## Installation

Unity Package Manager > Add package from git URL:

```
https://github.com/XuToWei/ReactiveBinding.git
```

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
}
```

## Features

- **Compile-time code generation** - Zero runtime reflection overhead
- **Multiple source types** - Fields, properties, and methods
- **Flexible callbacks** - 0, N, or 2N parameters
- **Multi-source binding** - Bind multiple sources to one callback
- **Auto-inference binding** - Automatically detect referenced sources from method body
- **First-call initialization** - Automatic initial callback trigger
- **Throttling** - Control observation frequency
- **Version containers** - VersionList, VersionDictionary, VersionHashSet with efficient version-based change detection
- **VersionField auto-generation** - Auto-generate properties from private fields with version tracking and parent chain propagation
- **Full diagnostics** - 25 compile-time error/warning codes

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

## VersionField Auto-Generation

Use `[VersionField]` to automatically generate properties from private fields with change tracking. When the property value changes, the version is incremented and propagated up through the parent chain.

### Basic Usage

```csharp
public partial class PlayerData : IVersion
{
    [VersionField] private int m_Health;
    [VersionField] private float m_Speed;
    [VersionField] private string m_Name;
}
```

### Generated Code

```csharp
partial class PlayerData
{
    private int __version;
    public ReactiveBinding.IVersion Parent { get; set; }
    public int Version => __version;

    public void IncrementVersion()
    {
        __version = ReactiveBinding.VersionCounter.Next();
        if (Parent != null) Parent.IncrementVersion();
    }

    public int Health
    {
        get => m_Health;
        set
        {
            if (value != m_Health)
            {
                m_Health = value;
                IncrementVersion();
            }
        }
    }

    public float Speed
    {
        get => m_Speed;
        set
        {
            if (System.Math.Abs(value - m_Speed) > 1e-6f)
            {
                m_Speed = value;
                IncrementVersion();
            }
        }
    }
    // ...
}
```

### Nested IVersion Fields

When a field type implements `IVersion`, the generator automatically manages the parent chain:

```csharp
public partial class GameData : IVersion
{
    [VersionField] private PlayerData m_Player;  // PlayerData : IVersion
}

// Generated setter:
public PlayerData Player
{
    get => m_Player;
    set
    {
        if (value != m_Player)
        {
            if (m_Player != null) m_Player.Parent = null;  // Clear old parent
            m_Player = value;
            if (value != null) value.Parent = this;        // Set new parent
            IncrementVersion();
        }
    }
}
```

### Version Propagation

Version changes propagate up through the entire parent chain:

```
GameData (Parent=null)
  └── PlayerData (Parent=GameData)
        └── WeaponData (Parent=PlayerData)

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
    [VersionField] private VersionList<ItemData> m_Items;
    [VersionField] private int m_Gold;
}

public partial class TeamData : IVersion
{
    [VersionField] private VersionDictionary<string, PlayerData> m_Players;
}
```

### Complex Hierarchy Example

A complete example with 3-level nesting and containers:

```csharp
// Level 3 - Leaf
public partial class SkillData : IVersion
{
    [VersionField] private int m_Damage;
    [VersionField] private float m_CoolDown;
}

// Level 2 - Middle (with container)
public partial class CharacterData : IVersion
{
    [VersionField] private int m_Health;
    [VersionField] private VersionList<SkillData> m_Skills;
}

// Level 1 - Root (with both single and container)
public partial class GameData : IVersion
{
    [VersionField] private CharacterData m_MainCharacter;
    [VersionField] private VersionList<CharacterData> m_AllCharacters;
}

// Usage:
var game = new GameData();
var player = new CharacterData();
var skill = new SkillData();

game.MainCharacter = player;        // player.Parent = game
player.Skills.Add(skill);           // skill.Parent = player.Skills, Skills.Parent = player

skill.Damage = 100;                 // All versions change:
                                    // skill.Version ↑
                                    // player.Skills.Version ↑
                                    // player.Version ↑
                                    // game.Version ↑
```

### Requirements

1. Class must be `partial`
2. Class must implement `IVersion`
3. Fields must have `m_` prefix
4. Fields must be `private`

## Version Containers

ReactiveBinding provides version-based containers for efficient collection change detection. Instead of comparing collection contents, only the version number is compared.

### Available Containers

- `VersionList<T>` - Implements `IList<T>, IVersion`
- `VersionDictionary<K,V>` - Implements `IDictionary<K,V>, IVersion`
- `VersionHashSet<T>` - Implements `ISet<T>, IVersion`

Each modification (Add, Remove, Clear, etc.) increments the `Version` property.

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

## IReactiveObserver Interface

Classes using `[ReactiveBind]` must implement `IReactiveObserver`. The Source Generator automatically implements `ObserveChanges()`.

```csharp
public interface IReactiveObserver
{
    void ObserveChanges();
}
```

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
| RB1001 | Error | Class must be partial |
| RB1002 | Error | Class must implement IReactiveObserver |
| RB1003 | Error | ReactiveThrottle value must be >= 1 |
| RB1004 | Error | ReactiveThrottle without IReactiveObserver |
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
| VF1001 | Error | VersionField class must be partial |
| VF1002 | Error | VersionField class must implement IVersion |
| VF2001 | Error | VersionField must have m_ prefix |
| VF2002 | Error | VersionField must be private |
| VF2003 | Error | Property name already exists |
| VF3001 | Error | Parent property access not allowed outside IVersion |
