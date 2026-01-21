# ReactiveBinding Samples

This folder contains Unity sample code demonstrating all ReactiveBinding features.

## Files

- **PlayerData.cs** - Comprehensive data model with all supported types
- **PlayerStatsUI.cs** - Main sample demonstrating all ReactiveBinding features
- **SampleTest.cs** - Test script with keyboard controls

## Supported Data Types

| Category | Types | Example |
|----------|-------|---------|
| Primitive | int, float, double, bool, string | `private int Health;` |
| Struct | Any struct, Vector3, custom structs | `private PlayerStats Stats;` |
| Enum | Any enum | `private PlayerState State;` |
| Nullable | int?, float?, etc. | `private int? TargetId;` |
| IVersion | VersionList, VersionDictionary, VersionHashSet | `private VersionList<Item> Items;` |

## Features Demonstrated

| Feature | Code Example |
|---------|--------------|
| Property source (int) | `[ReactiveSource] private int Health => playerData.Health;` |
| Property source (float) | `[ReactiveSource] private float Mana => playerData.Mana;` |
| Property source (string) | `[ReactiveSource] private string PlayerName => playerData.PlayerName;` |
| Property source (bool) | `[ReactiveSource] private bool IsAlive => playerData.IsAlive;` |
| Method source | `[ReactiveSource] private int GetTotalDamage() => ...;` |
| Struct source | `[ReactiveSource] private PlayerStats Stats => playerData.Stats;` |
| Vector3 source | `[ReactiveSource] private Vector3 Position => playerData.Position;` |
| Enum source | `[ReactiveSource] private PlayerState State => playerData.State;` |
| Nullable source | `[ReactiveSource] private int? TargetId => playerData.TargetId;` |
| VersionList source | `[ReactiveSource] private VersionList<Item> Items => playerData.Items;` |
| VersionDictionary source | `[ReactiveSource] private VersionDictionary<string, int> Skills => ...;` |
| VersionHashSet source | `[ReactiveSource] private VersionHashSet<string> Achievements => ...;` |
| Single bind (0 params) | `[ReactiveBind(nameof(Level))] void OnLevelChanged()` |
| Single bind (1 param) | `[ReactiveBind(nameof(Mana))] void OnManaChanged(float newValue)` |
| Single bind (2 params) | `[ReactiveBind(nameof(Health))] void OnHealthChanged(int old, int new)` |
| Struct bind (old/new) | `[ReactiveBind(nameof(Stats))] void OnStatsChanged(PlayerStats old, PlayerStats new)` |
| Enum bind (old/new) | `[ReactiveBind(nameof(State))] void OnStateChanged(PlayerState old, PlayerState new)` |
| Nullable bind | `[ReactiveBind(nameof(TargetId))] void OnTargetChanged(int? old, int? new)` |
| Multi bind (0 params) | `[ReactiveBind(nameof(Health), nameof(MaxHealth))] void OnChanged()` |
| Multi bind (N params) | `[ReactiveBind(nameof(Level), nameof(GetTotalDamage))] void OnChanged(int, int)` |
| Multi bind (2N params) | `[ReactiveBind(nameof(CriticalRate), nameof(GetTotalDamage))] void OnChanged(...)` |
| VersionList bind (0 params) | `[ReactiveBind(nameof(Items))] void OnItemsChanged()` |
| VersionList bind (1 param) | `[ReactiveBind(nameof(Items))] void OnItemsChanged(VersionList<Item> items)` |
| VersionDictionary bind | `[ReactiveBind(nameof(Skills))] void OnSkillsChanged(VersionDictionary<...> skills)` |
| VersionHashSet bind | `[ReactiveBind(nameof(Achievements))] void OnAchievementsChanged(...)` |
| Mixed binding | `[ReactiveBind(nameof(Skills), nameof(Level))] void OnChanged(VersionDictionary<...>, int)` |
| Throttle | `[ReactiveThrottle(2)] public partial class PlayerStatsUI` |

## Usage

1. Copy files to your Unity project's Assets folder
2. Create a new scene
3. Create an empty GameObject and add `PlayerStatsUI` component
4. Create another GameObject and add `SampleTest` component
5. Link the `PlayerStatsUI` reference in `SampleTest`
6. Play and press keys to test (see console for logs)

## Test Keys

| Key | Action | Type |
|-----|--------|------|
| **Basic Types** | | |
| D | Take 10 damage | int |
| H | Heal 20 HP | int |
| M | Use 10 mana | float |
| L | Level up | int + method |
| B | Add 5 bonus damage | method |
| C | Random critical rate | float |
| N | Random player name | string |
| K | Toggle alive state | bool |
| **Struct Types** | | |
| 1 | Modify player stats | PlayerStats |
| 2 | Move position | Vector3 |
| 3 | Equip weapon | EquipmentSlot |
| **Enum Types** | | |
| 4 | Change player state | PlayerState |
| 5 | Change element type | ElementType |
| **Nullable Types** | | |
| 6 | Set/clear target | int? |
| 7 | Set/clear buff duration | float? |
| **VersionList** | | |
| I | Add random item | VersionList |
| R | Remove last item | VersionList |
| X | Clear inventory | VersionList |
| **VersionDictionary** | | |
| Q | Learn random skill | VersionDictionary |
| W | Upgrade random skill | VersionDictionary |
| E | Forget random skill | VersionDictionary |
| **VersionHashSet** | | |
| A | Unlock random achievement | VersionHashSet |
| S | Revoke random achievement | VersionHashSet |
| **VersionList<Struct>** | | |
| Z | Add random buff | VersionList<BuffData> |
| V | Clear all buffs | VersionList<BuffData> |

## Notes

### Version Container Rules

1. **No old/new value pairs**: Version containers only support 0 or 1 parameter (the container itself), not old/new pairs
2. **Mixed bindings**: When combining Version containers with basic types, only N parameters are supported, not 2N
3. **Version tracking**: Changes are detected by comparing `Version` property, not collection contents

### Supported Types for ReactiveSource

Only the following types are allowed:
- Primitive types (int, float, double, bool, string, etc.)
- Struct types (including Unity types like Vector3)
- Enum types
- Nullable value types (int?, float?, etc.)
- Types implementing IVersion interface

**Not supported**: Reference types (class) unless they implement IVersion

### Struct Type Requirements

Custom struct types used with `[ReactiveSource]` must implement `==` and `!=` operators:

```csharp
public struct MyStruct
{
    public int Value;

    public static bool operator ==(MyStruct left, MyStruct right) => left.Value == right.Value;
    public static bool operator !=(MyStruct left, MyStruct right) => !(left == right);
    public override bool Equals(object obj) => obj is MyStruct other && this == other;
    public override int GetHashCode() => Value.GetHashCode();
}
```

If not implemented, the compiler will report error CS0019.
