using UnityEngine;
using UnityEngine.UI;

namespace ReactiveBinding.Samples
{
    /// <summary>
    /// Demonstrates all ReactiveBinding features.
    /// </summary>
    [ReactiveThrottle(2)] // Feature: Throttle - only check every 2nd call
    public partial class PlayerStatsUI : MonoBehaviour, IReactiveObserver
    {
        [Header("Data")]
        [SerializeField] private PlayerData playerData;

        [Header("UI References")]
        [SerializeField] private Slider healthBar;
        [SerializeField] private Slider manaBar;
        [SerializeField] private Text levelText;
        [SerializeField] private Text damageText;
        [SerializeField] private Text criticalText;
        [SerializeField] private Text playerNameText;
        [SerializeField] private Text statsText;
        [SerializeField] private Text inventoryText;

        private void Awake()
        {
            playerData = new PlayerData
            {
                // Basic types
                Health = 100,
                MaxHealth = 100,
                Mana = 50f,
                Level = 1,
                BaseDamage = 10,
                BonusDamage = 5,
                CriticalRate = 0.1f,
                PlayerName = "Player",
                IsAlive = true,
                // Nullable types
                TargetId = null,
                BuffDuration = null,
                // Struct types
                Position = Vector3.zero,
                Stats = new PlayerStats { Strength = 10, Agility = 8, Intelligence = 5 },
                CurrentWeapon = EquipmentSlot.Empty,
                // Enum types
                State = PlayerState.Idle,
                Element = ElementType.Fire
            };
        }

        private void Update()
        {
            // Call ObserveChanges every frame to check for data changes
            // Due to ReactiveThrottle(2), actual checks happen every 2nd call
            ObserveChanges();
        }

        #region ReactiveSource - Basic Types

        // Feature: Property source (int)
        [ReactiveSource]
        private int Health => playerData.Health;

        [ReactiveSource]
        private int MaxHealth => playerData.MaxHealth;

        // Feature: Property source with float type
        [ReactiveSource]
        private float Mana => playerData.Mana;

        [ReactiveSource]
        private int Level => playerData.Level;

        [ReactiveSource]
        private float CriticalRate => playerData.CriticalRate;

        // Feature: String type source
        [ReactiveSource]
        private string PlayerName => playerData.PlayerName;

        // Feature: Bool type source
        [ReactiveSource]
        private bool IsAlive => playerData.IsAlive;

        #endregion

        #region ReactiveSource - Method

        // Feature: Method source with complex calculation
        [ReactiveSource]
        private int GetTotalDamage() => playerData.BaseDamage + playerData.BonusDamage * playerData.Level;

        // Feature: Method source with health percentage calculation
        [ReactiveSource]
        private float GetHealthPercent() => playerData.MaxHealth > 0 ? (float)playerData.Health / playerData.MaxHealth : 0f;

        #endregion

        #region ReactiveSource - Struct Types

        // Feature: Custom struct type source
        [ReactiveSource]
        private PlayerStats Stats => playerData.Stats;

        // Feature: Unity built-in struct type
        [ReactiveSource]
        private Vector3 Position => playerData.Position;

        // Feature: Custom struct type
        [ReactiveSource]
        private EquipmentSlot CurrentWeapon => playerData.CurrentWeapon;

        #endregion

        #region ReactiveSource - Enum Types

        // Feature: Enum type source
        [ReactiveSource]
        private PlayerState State => playerData.State;

        [ReactiveSource]
        private ElementType Element => playerData.Element;

        #endregion

        #region ReactiveSource - Nullable Types

        // Feature: Nullable value type source
        [ReactiveSource]
        private int? TargetId => playerData.TargetId;

        [ReactiveSource]
        private float? BuffDuration => playerData.BuffDuration;

        #endregion

        #region ReactiveSource - Version Containers

        // Feature: VersionList source
        [ReactiveSource]
        private VersionList<Item> Items => playerData.Items;

        // Feature: VersionDictionary source
        [ReactiveSource]
        private VersionDictionary<string, int> Skills => playerData.Skills;

        // Feature: VersionHashSet source
        [ReactiveSource]
        private VersionHashSet<string> Achievements => playerData.Achievements;

        // Feature: VersionList with struct elements
        [ReactiveSource]
        private VersionList<BuffData> Buffs => playerData.Buffs;

        #endregion

        #region ReactiveBind - Single Source (Basic Types)

        // Feature: Single source, 2 parameters (old and new value)
        [ReactiveBind(nameof(Health))]
        private void OnHealthChanged(int oldValue, int newValue)
        {
            Debug.Log($"[int] Health changed: {oldValue} -> {newValue}");
            if (healthBar != null)
            {
                healthBar.value = GetHealthPercent();
            }
        }

        // Feature: Single source, 1 parameter (new value only)
        [ReactiveBind(nameof(Mana))]
        private void OnManaChanged(float newValue)
        {
            Debug.Log($"[float] Mana changed to: {newValue}");
            if (manaBar != null)
            {
                manaBar.value = newValue / 100f;
            }
        }

        // Feature: Single source, 0 parameters
        [ReactiveBind(nameof(Level))]
        private void OnLevelChanged()
        {
            Debug.Log($"[int] Level changed to: {Level}");
            if (levelText != null)
            {
                levelText.text = $"Lv.{Level}";
            }
        }

        // Feature: String type binding
        [ReactiveBind(nameof(PlayerName))]
        private void OnPlayerNameChanged(string oldName, string newName)
        {
            Debug.Log($"[string] Player name changed: {oldName} -> {newName}");
            if (playerNameText != null)
            {
                playerNameText.text = newName;
            }
        }

        // Feature: Bool type binding
        [ReactiveBind(nameof(IsAlive))]
        private void OnAliveChanged(bool oldValue, bool newValue)
        {
            Debug.Log($"[bool] IsAlive changed: {oldValue} -> {newValue}");
            if (!newValue && oldValue)
                Debug.Log("[bool] Player died!");
            else if (newValue && !oldValue)
                Debug.Log("[bool] Player resurrected!");
        }

        #endregion

        #region ReactiveBind - Single Source (Struct Types)

        // Feature: Struct type binding with old and new values
        [ReactiveBind(nameof(Stats))]
        private void OnStatsChanged(PlayerStats oldStats, PlayerStats newStats)
        {
            Debug.Log($"[struct] Stats changed: {oldStats} -> {newStats}");
            Debug.Log($"[struct] Total power: {oldStats.TotalPower} -> {newStats.TotalPower}");
        }

        // Feature: Vector3 struct binding
        [ReactiveBind(nameof(Position))]
        private void OnPositionChanged(Vector3 oldPos, Vector3 newPos)
        {
            Debug.Log($"[Vector3] Position changed: {oldPos} -> {newPos}");
            float distance = Vector3.Distance(oldPos, newPos);
            Debug.Log($"[Vector3] Moved distance: {distance:F2}");
        }

        // Feature: Custom struct binding with new value only
        [ReactiveBind(nameof(CurrentWeapon))]
        private void OnWeaponChanged(EquipmentSlot newWeapon)
        {
            Debug.Log($"[struct] Weapon equipped: {newWeapon}");
        }

        #endregion

        #region ReactiveBind - Single Source (Enum Types)

        // Feature: Enum binding with old and new values
        [ReactiveBind(nameof(State))]
        private void OnStateChanged(PlayerState oldState, PlayerState newState)
        {
            Debug.Log($"[enum] State changed: {oldState} -> {newState}");
        }

        // Feature: Enum binding with new value only
        [ReactiveBind(nameof(Element))]
        private void OnElementChanged(ElementType newElement)
        {
            Debug.Log($"[enum] Element changed to: {newElement}");
        }

        #endregion

        #region ReactiveBind - Single Source (Nullable Types)

        // Feature: Nullable type binding
        [ReactiveBind(nameof(TargetId))]
        private void OnTargetChanged(int? oldTarget, int? newTarget)
        {
            string oldStr = oldTarget.HasValue ? oldTarget.Value.ToString() : "None";
            string newStr = newTarget.HasValue ? newTarget.Value.ToString() : "None";
            Debug.Log($"[int?] Target changed: {oldStr} -> {newStr}");
        }

        // Feature: Nullable float binding
        [ReactiveBind(nameof(BuffDuration))]
        private void OnBuffDurationChanged(float? newDuration)
        {
            if (newDuration.HasValue)
                Debug.Log($"[float?] Buff duration: {newDuration.Value:F1}s");
            else
                Debug.Log("[float?] Buff expired");
        }

        #endregion

        #region ReactiveBind - Multi Source

        // Feature: Multi-source binding, 0 parameters
        [ReactiveBind(nameof(Health), nameof(MaxHealth))]
        private void OnHealthOrMaxHealthChanged()
        {
            Debug.Log($"[multi] Health or MaxHealth changed, current: {Health}/{MaxHealth}");
        }

        // Feature: Multi-source binding, N parameters (new values only)
        [ReactiveBind(nameof(Level), nameof(GetTotalDamage))]
        private void OnCombatStatsChanged(int newLevel, int newDamage)
        {
            Debug.Log($"[multi] Combat stats changed - Level: {newLevel}, Damage: {newDamage}");
            if (damageText != null)
            {
                damageText.text = $"DMG: {newDamage}";
            }
        }

        // Feature: Multi-source binding, 2N parameters (old and new value pairs)
        [ReactiveBind(nameof(CriticalRate), nameof(GetTotalDamage))]
        private void OnDamageStatsChanged(float oldCrit, float newCrit, int oldDamage, int newDamage)
        {
            Debug.Log($"[multi] Damage stats - Crit: {oldCrit:P} -> {newCrit:P}, Damage: {oldDamage} -> {newDamage}");
            if (criticalText != null)
            {
                criticalText.text = $"CRIT: {newCrit:P0}";
            }
        }

        // Feature: Multi-source binding with 3 sources
        [ReactiveBind(nameof(Health), nameof(Mana), nameof(Level))]
        private void OnAllStatsChanged(int newHealth, float newMana, int newLevel)
        {
            Debug.Log($"[multi] Stats changed - HP: {newHealth}, MP: {newMana}, Lv: {newLevel}");
            if (statsText != null)
            {
                statsText.text = $"HP:{newHealth} MP:{newMana:F0} Lv:{newLevel}";
            }
        }

        #endregion

        #region ReactiveBind - Version Containers

        // Feature: VersionList binding with no parameters
        [ReactiveBind(nameof(Items))]
        private void OnItemsChanged()
        {
            Debug.Log($"[VersionList] Inventory changed! Count: {Items.Count}");
        }

        // Feature: VersionList binding with container parameter
        [ReactiveBind(nameof(Items))]
        private void OnItemsChangedWithParam(VersionList<Item> items)
        {
            Debug.Log($"[VersionList] Inventory updated - {items.Count} items");
            if (inventoryText != null)
            {
                inventoryText.text = $"Items: {items.Count}";
            }
        }

        // Feature: VersionDictionary binding with container parameter
        [ReactiveBind(nameof(Skills))]
        private void OnSkillsChanged(VersionDictionary<string, int> skills)
        {
            Debug.Log($"[VersionDictionary] Skills updated - Count: {skills.Count}, Version: {skills.Version}");
            foreach (var skill in skills)
            {
                Debug.Log($"  - {skill.Key}: Lv.{skill.Value}");
            }
        }

        // Feature: VersionHashSet binding with container parameter
        [ReactiveBind(nameof(Achievements))]
        private void OnAchievementsChanged(VersionHashSet<string> achievements)
        {
            Debug.Log($"[VersionHashSet] Achievements updated - Count: {achievements.Count}, Version: {achievements.Version}");
            foreach (var achievement in achievements)
            {
                Debug.Log($"  - {achievement}");
            }
        }

        // Feature: VersionList with struct elements
        [ReactiveBind(nameof(Buffs))]
        private void OnBuffsChanged(VersionList<BuffData> buffs)
        {
            Debug.Log($"[VersionList<struct>] Buffs updated - Count: {buffs.Count}");
            foreach (var buff in buffs)
            {
                Debug.Log($"  - {buff}");
            }
        }

        #endregion

        #region ReactiveBind - Mixed Bindings (Version Container + Basic Types)

        // Feature: Mixed binding - Version container + basic type
        [ReactiveBind(nameof(Skills), nameof(Level))]
        private void OnSkillsAndLevelChanged(VersionDictionary<string, int> skills, int level)
        {
            Debug.Log($"[mixed] Skills or Level changed - Skills count: {skills.Count}, Level: {level}");
        }

        // Feature: Mixed binding - Multiple Version containers
        [ReactiveBind(nameof(Items), nameof(Achievements))]
        private void OnItemsOrAchievementsChanged()
        {
            Debug.Log($"[mixed] Items or Achievements changed - Items: {Items.Count}, Achievements: {Achievements.Count}");
        }

        // Feature: Mixed binding - Version container + enum
        [ReactiveBind(nameof(Buffs), nameof(State))]
        private void OnBuffsOrStateChanged(VersionList<BuffData> buffs, PlayerState state)
        {
            Debug.Log($"[mixed] Buffs: {buffs.Count}, State: {state}");
        }

        #endregion

        #region ReactiveBind - Auto Inference

        // Feature: Auto-inferred single source binding
        // The generator automatically detects that this method references Health
        [ReactiveBind] // No parameters - auto-infer from method body
        private void OnHealthChangedAuto()
        {
            // References Health field - automatically bound to Health source
            Debug.Log($"[auto] Health is now: {Health}");
        }

        // Feature: Auto-inferred multi-source binding
        // The generator detects references to both Health and Mana
        [ReactiveBind] // Auto-infer multiple sources
        private void OnHealthAndManaChangedAuto()
        {
            // References both Health and Mana - bound to both sources
            var total = Health + (int)Mana;
            Debug.Log($"[auto-multi] HP + MP = {total}");
        }

        // Feature: Auto-inference with this. access
        [ReactiveBind]
        private void OnLevelChangedAuto()
        {
            // Using this.Level - still detected as Level source
            Debug.Log($"[auto-this] Level changed to: {this.Level}");
        }

        // Feature: Auto-inference with method source
        [ReactiveBind]
        private void OnDamageChangedAuto()
        {
            // References GetTotalDamage() method source
            var damage = GetTotalDamage();
            Debug.Log($"[auto-method] Total damage: {damage}");
        }

        // Feature: Auto-inference ignores non-source members
        [ReactiveBind]
        private void OnStatsChangedAuto()
        {
            // Only Stats is a ReactiveSource, playerData is not
            // So this only binds to Stats
            var stats = Stats;
            var raw = playerData; // This is ignored (not a ReactiveSource)
            Debug.Log($"[auto-ignore] Stats power: {stats.TotalPower}");
        }

        // Feature: Auto-inference with expression body
        [ReactiveBind]
        private void OnCriticalChangedAuto() => Debug.Log($"[auto-expr] Critical rate: {CriticalRate:P}");

        // Feature: Auto-inference with lambda containing source reference
        [ReactiveBind]
        private void OnPositionChangedAuto()
        {
            // Source reference inside lambda is also detected
            System.Func<string> getPos = () => Position.ToString();
            Debug.Log($"[auto-lambda] Position: {getPos()}");
        }

        // Feature: Auto-inference with Version container
        [ReactiveBind]
        private void OnItemsChangedAuto()
        {
            // References Items (VersionList) - auto-bound
            Debug.Log($"[auto-version] Item count: {Items.Count}");
        }

        // Feature: Auto-inference mixed Version container and basic type
        [ReactiveBind]
        private void OnSkillsAndStateChangedAuto()
        {
            // References both Skills (VersionDictionary) and State (enum)
            Debug.Log($"[auto-mixed] Skills: {Skills.Count}, State: {State}");
        }

        #endregion

        #region Public Methods for Testing - Basic Types

        public void TakeDamage(int damage)
        {
            playerData.Health = Mathf.Max(0, playerData.Health - damage);
        }

        public void Heal(int amount)
        {
            playerData.Health = Mathf.Min(playerData.MaxHealth, playerData.Health + amount);
        }

        public void UseMana(float amount)
        {
            playerData.Mana = Mathf.Max(0, playerData.Mana - amount);
        }

        public void LevelUp()
        {
            playerData.Level++;
            playerData.MaxHealth += 10;
            playerData.BaseDamage += 2;
        }

        public void SetPlayerName(string name)
        {
            playerData.PlayerName = name;
        }

        public void AddBonusDamage(int bonus)
        {
            playerData.BonusDamage += bonus;
        }

        public void SetCriticalRate(float rate)
        {
            playerData.CriticalRate = Mathf.Clamp01(rate);
        }

        public void SetAlive(bool alive)
        {
            playerData.IsAlive = alive;
        }

        #endregion

        #region Public Methods for Testing - Struct Types

        public void ModifyStats(int str, int agi, int intel)
        {
            playerData.Stats = new PlayerStats { Strength = str, Agility = agi, Intelligence = intel };
        }

        public void MovePosition(Vector3 newPosition)
        {
            playerData.Position = newPosition;
        }

        public void EquipWeapon(string name, int atk, int def)
        {
            playerData.CurrentWeapon = new EquipmentSlot { ItemName = name, AttackBonus = atk, DefenseBonus = def };
        }

        #endregion

        #region Public Methods for Testing - Enum Types

        public void SetState(PlayerState state)
        {
            playerData.State = state;
        }

        public void SetElement(ElementType element)
        {
            playerData.Element = element;
        }

        #endregion

        #region Public Methods for Testing - Nullable Types

        public void SetTarget(int? targetId)
        {
            playerData.TargetId = targetId;
        }

        public void SetBuffDuration(float? duration)
        {
            playerData.BuffDuration = duration;
        }

        #endregion

        #region Public Methods for Testing - Version Containers

        public void AddItem(string name, int quantity = 1)
        {
            playerData.Items.Add(new Item(name, quantity));
        }

        public void RemoveLastItem()
        {
            if (playerData.Items.Count > 0)
            {
                playerData.Items.RemoveAt(playerData.Items.Count - 1);
            }
        }

        public void ClearInventory()
        {
            playerData.Items.Clear();
        }

        public void LearnSkill(string skillName, int level = 1)
        {
            playerData.Skills[skillName] = level;
        }

        public void ForgetSkill(string skillName)
        {
            playerData.Skills.Remove(skillName);
        }

        public void UpgradeSkill(string skillName)
        {
            if (playerData.Skills.TryGetValue(skillName, out int level))
            {
                playerData.Skills[skillName] = level + 1;
            }
        }

        public void UnlockAchievement(string achievement)
        {
            playerData.Achievements.Add(achievement);
        }

        public void RevokeAchievement(string achievement)
        {
            playerData.Achievements.Remove(achievement);
        }

        public void AddBuff(string name, float duration, int effectValue)
        {
            playerData.Buffs.Add(new BuffData(name, duration, effectValue));
        }

        public void ClearBuffs()
        {
            playerData.Buffs.Clear();
        }

        #endregion
    }
}
