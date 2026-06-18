using UnityEngine;

namespace ReactiveBinding.Samples
{
    /// <summary>
    /// Sample data model for player stats.
    /// </summary>
    public class PlayerData
    {
        // Basic types
        public int Health;
        public int MaxHealth;
        public float Mana;
        public int Level;
        public int BaseDamage;
        public int BonusDamage;
        public float CriticalRate;
        public string PlayerName;
        public bool IsAlive;

        // Nullable types
        public int? TargetId;
        public float? BuffDuration;

        // Struct types
        public Vector3 Position;
        public PlayerStats Stats;
        public EquipmentSlot CurrentWeapon;

        // Enum types
        public PlayerState State;
        public ElementType Element;

        // Version containers
        public VersionList<Item> Items = new();
        public VersionDictionary<string, int> Skills = new();
        public VersionHashSet<string> Achievements = new();
        public VersionList<BuffData> Buffs = new();
    }

    /// <summary>
    /// Sample struct for player statistics.
    /// Struct used with ReactiveSource must implement == and != operators.
    /// </summary>
    public struct PlayerStats
    {
        public int Strength;
        public int Agility;
        public int Intelligence;

        public int TotalPower => Strength + Agility + Intelligence;

        public override string ToString() => $"STR:{Strength} AGI:{Agility} INT:{Intelligence}";

        public static bool operator ==(PlayerStats left, PlayerStats right) =>
            left.Strength == right.Strength && left.Agility == right.Agility && left.Intelligence == right.Intelligence;

        public static bool operator !=(PlayerStats left, PlayerStats right) => !(left == right);

        public override bool Equals(object obj) => obj is PlayerStats other && this == other;

        public override int GetHashCode() => (Strength, Agility, Intelligence).GetHashCode();
    }

    /// <summary>
    /// Sample struct for equipment slot.
    /// Struct used with ReactiveSource must implement == and != operators.
    /// </summary>
    public struct EquipmentSlot
    {
        public string ItemName;
        public int AttackBonus;
        public int DefenseBonus;

        public static EquipmentSlot Empty => new() { ItemName = "None", AttackBonus = 0, DefenseBonus = 0 };

        public override string ToString() => $"{ItemName} (ATK+{AttackBonus}, DEF+{DefenseBonus})";

        public static bool operator ==(EquipmentSlot left, EquipmentSlot right) =>
            left.ItemName == right.ItemName && left.AttackBonus == right.AttackBonus && left.DefenseBonus == right.DefenseBonus;

        public static bool operator !=(EquipmentSlot left, EquipmentSlot right) => !(left == right);

        public override bool Equals(object obj) => obj is EquipmentSlot other && this == other;

        public override int GetHashCode() => (ItemName, AttackBonus, DefenseBonus).GetHashCode();
    }

    /// <summary>
    /// Sample struct for buff data.
    /// Struct used with ReactiveSource must implement == and != operators.
    /// </summary>
    public struct BuffData
    {
        public string Name;
        public float Duration;
        public int EffectValue;

        public BuffData(string name, float duration, int effectValue)
        {
            Name = name;
            Duration = duration;
            EffectValue = effectValue;
        }

        public override string ToString() => $"{Name} ({Duration}s, +{EffectValue})";

        public static bool operator ==(BuffData left, BuffData right) =>
            left.Name == right.Name && left.Duration == right.Duration && left.EffectValue == right.EffectValue;

        public static bool operator !=(BuffData left, BuffData right) => !(left == right);

        public override bool Equals(object obj) => obj is BuffData other && this == other;

        public override int GetHashCode() => (Name, Duration, EffectValue).GetHashCode();
    }

    /// <summary>
    /// Sample enum for player state.
    /// </summary>
    public enum PlayerState
    {
        Idle,
        Walking,
        Running,
        Attacking,
        Dead
    }

    /// <summary>
    /// Sample enum for element type.
    /// </summary>
    public enum ElementType
    {
        None,
        Fire,
        Water,
        Earth,
        Wind
    }

    /// <summary>
    /// Sample item class for inventory.
    /// </summary>
    public class Item
    {
        public string Name;
        public int Quantity;

        public Item(string name, int quantity = 1)
        {
            Name = name;
            Quantity = quantity;
        }

        public override string ToString() => $"{Name} x{Quantity}";
    }
}
