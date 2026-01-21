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
- **Full diagnostics** - 20 compile-time error/warning codes

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
