# ReactiveBinding Samples

This folder contains Unity sample code demonstrating all ReactiveBinding features.

## Files

- **PlayerData.cs** - Simple data model class
- **PlayerStatsUI.cs** - Main sample demonstrating all features
- **SampleTest.cs** - Test script for runtime testing

## Features Demonstrated

### PlayerStatsUI.cs

| Feature | Code Example |
|---------|--------------|
| Field source | `[ReactiveSource] private int Health => playerData.Health;` |
| Property source | `[ReactiveSource] private float Mana => playerData.Mana;` |
| Method source | `[ReactiveSource] private int GetTotalDamage() => ...;` |
| Single bind (0 params) | `[ReactiveBind(nameof(Level))] void OnLevelChanged()` |
| Single bind (1 param) | `[ReactiveBind(nameof(Mana))] void OnManaChanged(float newValue)` |
| Single bind (2 params) | `[ReactiveBind(nameof(Health))] void OnHealthChanged(int old, int new)` |
| Multi bind (0 params) | `[ReactiveBind(nameof(Health), nameof(MaxHealth))] void OnChanged()` |
| Multi bind (N params) | `[ReactiveBind(nameof(Level), nameof(GetTotalDamage))] void OnChanged(int, int)` |
| Multi bind (2N params) | `[ReactiveBind(nameof(CriticalRate), nameof(GetTotalDamage))] void OnChanged(float, float, int, int)` |
| Throttle | `[ReactiveThrottle(2)] public partial class PlayerStatsUI` |
| Float comparison | Automatic epsilon comparison for `float` and `double` types |

## Usage

1. Copy files to your Unity project's Assets folder
2. Create a new scene
3. Create an empty GameObject and add `PlayerStatsUI` component
4. Create another GameObject and add `SampleTest` component
5. Link the `PlayerStatsUI` reference in `SampleTest`
6. Play and press keys to test (see console for logs)

## Test Keys

| Key | Action |
|-----|--------|
| D | Take 10 damage |
| H | Heal 20 HP |
| M | Use 10 mana |
| L | Level up |
| B | Add 5 bonus damage |
| C | Random critical rate |
| N | Random player name |
