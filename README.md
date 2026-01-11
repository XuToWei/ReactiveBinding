# ReactiveBinding

A compile-time reactive data binding system using C# Source Generator.

## Overview

ReactiveBinding provides attribute-based reactive data binding that generates change detection code at compile time. This eliminates the need for manual change detection logic while avoiding runtime reflection overhead.

## Quick Start

```csharp
using ReactiveBinding;

public partial class PlayerUI : IReactiveObserver
{
    private PlayerData playerData;

    // Mark data sources with [ReactiveSource]
    [ReactiveSource]
    private int Health => playerData.Health;

    [ReactiveSource]
    private int GetMana() => playerData.Mana;

    // Mark callbacks with [ReactiveBind]
    [ReactiveBind(nameof(Health))]
    private void OnHealthChanged(int oldValue, int newValue)
    {
        healthBar.SetValue(newValue);
    }

    [ReactiveBind(nameof(GetMana))]
    private void OnManaChanged(int newValue)
    {
        manaBar.SetValue(newValue);
    }
}

// Usage
void Update()
{
    playerUI.ObserveChanges(); // Call to check for changes
}
```

## Features

- **Compile-time code generation** - Zero runtime reflection overhead
- **Multiple source types** - Fields, properties, and methods
- **Flexible callbacks** - 0, N, or 2N parameters
- **Multi-source binding** - Bind multiple sources to one callback
- **First-call initialization** - Automatic initial callback trigger
- **Throttling** - Control observation frequency
- **Full diagnostics** - 17 compile-time error/warning codes

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

### ReactiveThrottleAttribute

Controls how often `ObserveChanges()` actually performs checks.

```csharp
[ReactiveThrottle(10)]  // Only check every 10th call
public partial class PlayerUI : IReactiveObserver
{
    // ...
}
```

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
3. `[ReactiveBind]` must use `nameof()` expressions
4. `[ReactiveSource]` methods must have return values and no parameters
5. `[ReactiveSource]` properties must have getters

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
| RB3001 | Error | ReactiveBind has no identities |
| RB3002 | Error | ReactiveBind method is static |
| RB3003 | Error | ReactiveBind method doesn't return void |
| RB3004 | Error | Invalid parameter count |
| RB3005 | Error | Parameter type mismatch |
| RB3006 | Error | Duplicate identities |
| RB3007 | Error | Not using nameof() |

## Generated Code Example

For a class like:

```csharp
public partial class Example : IReactiveObserver
{
    [ReactiveSource]
    public int Value;

    [ReactiveBind(nameof(Value))]
    private void OnChanged(int oldValue, int newValue) { }
}
```

The generator creates:

```csharp
partial class Example
{
    private bool __reactive_initialized;
    private int __reactive_Value;

    public void ObserveChanges()
    {
        if (!__reactive_initialized)
        {
            __reactive_initialized = true;
            __reactive_Value = Value;
            OnChanged(default, Value);
            return;
        }

        int __old_Value = __reactive_Value;
        if (!object.Equals(Value, __reactive_Value))
        {
            __reactive_Value = Value;
            OnChanged(__old_Value, Value);
        }
    }
}
```
