# ReactiveBinding

中文 | [English](README.md)

基于 C# Source Generator 的编译时响应式数据绑定系统。

## 概述

ReactiveBinding 提供基于特性的响应式数据绑定，在编译时生成变更检测代码。无需手动编写变更检测逻辑，同时避免运行时反射开销。

## 交流QQ群：949482664

## 安装

Unity Package Manager > Add package from git URL:

```
https://github.com/XuToWei/ReactiveBinding.git
```

## 快速开始

```csharp
using ReactiveBinding;

public partial class PlayerUI : IReactiveObserver
{
    private PlayerData playerData;

    // 属性数据源
    [ReactiveSource]
    private int Health => playerData.Health;

    // 方法数据源（支持复杂计算逻辑）
    [ReactiveSource]
    private int GetTotalDamage() => playerData.BaseDamage + playerData.BonusDamage * playerData.DamageMultiplier;

    // 单数据源绑定
    [ReactiveBind(nameof(Health))]
    private void OnHealthChanged(int oldValue, int newValue)
    {
        healthBar.SetValue(newValue);
    }

    // 多数据源绑定 - 任意数据源变化时触发
    [ReactiveBind(nameof(Health), nameof(GetTotalDamage))]
    private void OnStatsChanged(int newHealth, int newDamage)
    {
        statsText.text = $"HP: {newHealth} DMG: {newDamage}";
    }

    // 自动推断绑定 - 自动检测引用的数据源
    [ReactiveBind]
    private void OnCombatStatsChanged()
    {
        // 自动绑定到 Health 和 GetTotalDamage
        var ratio = Health / (float)GetTotalDamage();
        combatRating.SetValue(ratio);
    }
}

// 使用方式
void Update()
{
    playerUI.ObserveChanges();
}
```

生成的代码：

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
            OnCombatStatsChanged();  // 自动推断绑定
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
            OnCombatStatsChanged();  // 自动推断绑定
        }
    }
}
```

## 特性

- **编译时代码生成** - 零运行时反射开销
- **多种数据源类型** - 支持字段、属性和方法
- **灵活的回调签名** - 支持 0、N 或 2N 个参数
- **多数据源绑定** - 多个数据源绑定到一个回调
- **自动推断绑定** - 自动分析方法体内引用的数据源
- **首次调用初始化** - 自动触发初始回调
- **节流控制** - 控制观察频率
- **版本容器** - VersionList、VersionDictionary、VersionHashSet，基于版本号的高效变更检测
- **VersionField 自动生成** - 从私有字段自动生成属性，支持版本追踪和父级链传播
- **完整诊断** - 25 个编译时错误/警告代码

## 特性说明

### ReactiveSourceAttribute

将字段、属性或方法标记为响应式数据源。

```csharp
[ReactiveSource]
public int Health;              // 字段

[ReactiveSource]
public int Mana => _mana;       // 属性

[ReactiveSource]
private int GetLevel() => _level;  // 方法（必须有返回值，无参数）
```

### ReactiveBindAttribute

将方法标记为数据变更的回调。使用 `nameof()` 指定数据源。

**回调签名：**
- `void Method()` - 无参数
- `void Method(T newValue)` - 仅新值（单数据源）
- `void Method(T1 new1, T2 new2)` - 仅新值（多数据源）
- `void Method(T old, T new)` - 旧值和新值（单数据源）
- `void Method(T1 old1, T1 new1, T2 old2, T2 new2)` - 旧值新值对（多数据源）

```csharp
// 单数据源，旧值和新值
[ReactiveBind(nameof(Health))]
private void OnHealthChanged(int oldValue, int newValue) { }

// 多数据源，无参数
[ReactiveBind(nameof(Health), nameof(Mana))]
private void OnStatsChanged() { }

// 多数据源，仅新值
[ReactiveBind(nameof(Health), nameof(Mana))]
private void OnStatsChangedNew(int newHealth, int newMana) { }
```

#### 自动推断模式

当 `[ReactiveBind]` 不带参数使用时，生成器会自动分析方法体，找出引用了哪些 `[ReactiveSource]` 成员：

```csharp
[ReactiveSource]
private int Health => playerData.Health;

[ReactiveSource]
private int Mana => playerData.Mana;

// 自动推断：检测方法体内的 Health 和 Mana 引用
[ReactiveBind]
private void OnStatsChanged()
{
    var total = Health + Mana;  // 两者都会自动绑定
    UpdateUI(total);
}
```

**注意事项：**
- 自动推断的方法必须**无参数**
- 支持：直接访问（`Health`）、this 访问（`this.Health`）、方法调用（`GetDamage()`）
- 正确处理局部变量遮蔽

### ReactiveThrottleAttribute

控制 `ObserveChanges()` 实际执行检查的频率。

```csharp
[ReactiveThrottle(10)]  // 每 10 次调用才执行一次检查
public partial class PlayerUI : IReactiveObserver
{
    // ...
}
```

## VersionField 自动生成

使用 `[VersionField]` 从私有字段自动生成带变更追踪的属性。当属性值变化时，版本号会递增并向上传播到整个父级链。

### 基本用法

```csharp
public partial class PlayerData : IVersion
{
    [VersionField] private int m_Health;
    [VersionField] private float m_Speed;
    [VersionField] private string m_Name;
}
```

### 生成的代码

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

### 嵌套 IVersion 字段

当字段类型实现了 `IVersion` 时，生成器会自动管理父级链：

```csharp
public partial class GameData : IVersion
{
    [VersionField] private PlayerData m_Player;  // PlayerData : IVersion
}

// 生成的 setter：
public PlayerData Player
{
    get => m_Player;
    set
    {
        if (value != m_Player)
        {
            if (m_Player != null) m_Player.Parent = null;  // 清除旧的父级
            m_Player = value;
            if (value != null) value.Parent = this;        // 设置新的父级
            IncrementVersion();
        }
    }
}
```

### 版本传播

版本变化会向上传播到整个父级链：

```
GameData (Parent=null)
  └── PlayerData (Parent=GameData)
        └── WeaponData (Parent=PlayerData)

当 WeaponData.Damage 变化时：
  → WeaponData.Version 变化
  → PlayerData.Version 变化
  → GameData.Version 变化
```

### 容器字段

版本容器也可以作为字段使用，自动管理父级链：

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

### 复杂层级示例

完整的 3 层嵌套和容器示例：

```csharp
// 第 3 层 - 叶子节点
public partial class SkillData : IVersion
{
    [VersionField] private int m_Damage;
    [VersionField] private float m_CoolDown;
}

// 第 2 层 - 中间层（带容器）
public partial class CharacterData : IVersion
{
    [VersionField] private int m_Health;
    [VersionField] private VersionList<SkillData> m_Skills;
}

// 第 1 层 - 根节点（同时有单个字段和容器）
public partial class GameData : IVersion
{
    [VersionField] private CharacterData m_MainCharacter;
    [VersionField] private VersionList<CharacterData> m_AllCharacters;
}

// 使用方式：
var game = new GameData();
var player = new CharacterData();
var skill = new SkillData();

game.MainCharacter = player;        // player.Parent = game
player.Skills.Add(skill);           // skill.Parent = player.Skills, Skills.Parent = player

skill.Damage = 100;                 // 所有版本都会变化：
                                    // skill.Version ↑
                                    // player.Skills.Version ↑
                                    // player.Version ↑
                                    // game.Version ↑
```

### 使用要求

1. 类必须是 `partial`
2. 类必须实现 `IVersion`
3. 字段必须有 `m_` 前缀
4. 字段必须是 `private`

## 版本容器

ReactiveBinding 提供基于版本号的容器，用于高效的集合变更检测。只比较版本号，而不是比较集合内容。

### 可用容器

- `VersionList<T>` - 实现 `IList<T>, IVersion`
- `VersionDictionary<K,V>` - 实现 `IDictionary<K,V>, IVersion`
- `VersionHashSet<T>` - 实现 `ISet<T>, IVersion`

每次修改操作（Add、Remove、Clear 等）都会递增 `Version` 属性。

### 使用示例

```csharp
public partial class InventoryUI : MonoBehaviour, IReactiveObserver
{
    [ReactiveSource]
    private VersionList<Item> Items = new();

    // 无参数 - 仅通知变更
    [ReactiveBind(nameof(Items))]
    private void OnItemsChanged()
    {
        RefreshUI();
    }

    // 带容器参数 - 接收容器本身
    [ReactiveBind(nameof(Items))]
    private void OnItemsChangedWithParam(VersionList<Item> items)
    {
        Debug.Log($"物品数量: {items.Count}");
    }

    void Update() => ObserveChanges();
}
```

### 生成的代码

```csharp
partial class InventoryUI
{
    private bool __reactive_initialized;
    private int __reactive_Items_version = -1;  // 存储版本号，而非内容

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

### 版本容器的回调签名

- `void Method()` - 无参数
- `void Method(ContainerType container)` - 接收容器本身

### 版本容器与基础类型混合绑定

版本容器可以与基础类型在多数据源绑定中组合使用：

```csharp
[ReactiveSource]
private VersionList<Item> Items = new();

[ReactiveSource]
private int TotalCount;

// 混合绑定 - 版本容器接收容器本身，基础类型接收新值
[ReactiveBind(nameof(Items), nameof(TotalCount))]
private void OnDataChanged(VersionList<Item> items, int count)
{
    Debug.Log($"物品: {items.Count}, 总数: {count}");
}
```

> 注意：当版本容器与基础类型混合使用时，不支持 2N 参数（旧值/新值对），因为版本容器无法追踪先前状态。

## IReactiveObserver 接口

使用 `[ReactiveBind]` 的类必须实现 `IReactiveObserver`。Source Generator 会自动实现 `ObserveChanges()`。

```csharp
public interface IReactiveObserver
{
    void ObserveChanges();
}
```

## 使用要求

1. 类必须声明为 `partial`
2. 类必须实现 `IReactiveObserver`
3. `[ReactiveBind]` 显式指定数据源时必须使用 `nameof()` 表达式（或使用无参数的自动推断模式）
4. `[ReactiveSource]` 方法必须有返回值且无参数
5. `[ReactiveSource]` 属性必须有 getter
6. 自定义 struct 类型必须实现 `==` 和 `!=` 运算符

## 编译器诊断

| 代码 | 类型 | 描述 |
|------|------|-------------|
| RB0001 | 警告 | ReactiveSource 没有对应的 ReactiveBind |
| RB0002 | 错误 | ReactiveBind 引用了不存在的数据源 |
| RB1001 | 错误 | 类必须是 partial |
| RB1002 | 错误 | 类必须实现 IReactiveObserver |
| RB1003 | 错误 | ReactiveThrottle 值必须 >= 1 |
| RB1004 | 错误 | ReactiveThrottle 需要实现 IReactiveObserver |
| RB2001 | 错误 | ReactiveSource 方法返回 void |
| RB2002 | 错误 | ReactiveSource 属性没有 getter |
| RB2003 | 错误 | ReactiveSource 方法有参数 |
| RB2004 | 错误 | 不支持的 ReactiveSource 类型 |
| RB2005 | 错误 | 结构体缺少相等运算符 |
| RB3001 | 错误 | ReactiveBind 没有指定数据源 |
| RB3002 | 错误 | ReactiveBind 方法是静态的 |
| RB3003 | 错误 | ReactiveBind 方法返回值不是 void |
| RB3004 | 错误 | 参数数量无效 |
| RB3005 | 错误 | 参数类型不匹配 |
| RB3006 | 错误 | 重复的数据源标识 |
| RB3007 | 错误 | 未使用 nameof() |
| RB3008 | 错误 | 自动推断未在方法体中找到数据源 |
| RB3009 | 错误 | 自动推断的方法不能有参数 |
| VF1001 | 错误 | VersionField 类必须是 partial |
| VF1002 | 错误 | VersionField 类必须实现 IVersion |
| VF2001 | 错误 | VersionField 必须有 m_ 前缀 |
| VF2002 | 错误 | VersionField 必须是 private |
| VF2003 | 错误 | 属性名已存在 |
| VF3001 | 错误 | Parent 属性只能在 IVersion 实现内部访问 |
