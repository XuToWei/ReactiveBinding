# ReactiveBinding

中文 | [English](README.md)

基于 C# Source Generator 的编译时响应式数据绑定系统。

## 概述

ReactiveBinding 提供基于特性的响应式数据绑定，在编译时生成变更检测代码。无需手动编写变更检测逻辑，同时避免运行时反射开销。

## 交流QQ群：949482664

## 安装

### Unity (UPM)

Unity Package Manager > Add package from git URL:

```
https://github.com/XuToWei/ReactiveBinding.git?path=Unity
```

### .NET (NuGet)

用于非 Unity 的 .NET 工程(或通过 [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) 在 Unity 中使用):

```bash
dotnet add package XuToWei.ReactiveBinding
```

NuGet 包同时包含运行时类型和源生成器,无需额外配置。

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

    public void ResetChanges()
    {
        __reactive_initialized = false;
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
- **重置支持** - `ResetChanges()` 支持对象池/复用场景
- **继承支持** - 派生类可以添加自己的响应式成员，自动链式调用基类
- **节流控制** - 控制观察频率
- **版本容器** - VersionList、VersionDictionary、VersionHashSet，基于版本号的高效变更检测
- **VersionField 自动生成** - 从私有字段自动生成属性，支持版本追踪和父级链传播
- **数据同步** - 类声明 `: IVersionSync` 即同步其所有 `[VersionField]`；`SyncContext` 扁平注册表做直接写入同步——每个改动当场把记录写进上下文的流，可作为全量快照或增量取出
- **自定义属性特性** - `[VersionFieldProperty]` 为生成的属性添加自定义特性（支持 `Type` 和 `string` 两种方式）
- **完整诊断** - 34 个编译时错误/警告代码

## AI 友好

专为 AI 辅助开发设计（Claude、Cursor、GitHub Copilot 等）：

| 传统方式 | ReactiveBinding + AI |
|---------|---------------------|
| 手写变更检测逻辑 | 声明 `[ReactiveSource]` 和 `[ReactiveBind]`，完事 |
| 维护 `OnXxxChanged` → `UpdateYyy` → `RefreshZzz` 调用链 | 自动触发，零维护 |
| 追踪复杂调用栈排查问题 | 只需确认绑定数据是否正确，AI 自行推断 |
| 忘记取消订阅事件导致内存泄漏 | 无需订阅管理，轮询 `ObserveChanges()` 即可 |
| 更新逻辑分散在多个文件 | 所有绑定通过特性集中在一个类中，一目了然 |

**为什么 AI + ReactiveBinding 配合得这么好：**

1. **所见即所得** - 生成的 `.g.cs` 文件是纯 C#，AI 可以直接阅读和推理
2. **快速失败** - 31 个编译时诊断在运行前捕获错误，AI 获得即时反馈
3. **最小上下文** - AI 只需理解"数据源 → 回调"，无需了解框架内部实现
4. **自文档化** - 特性清晰表达意图："当 X 变化时，调用 Y"

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

### ReactiveObserveIgnoreAttribute

当类内部没有调用 `ObserveChanges()` 时，忽略 RB0003 错误。适用于 `ObserveChanges()` 由外部调用的场景（如管理器或框架统一调用）。

```csharp
[ReactiveObserveIgnore]
public partial class PlayerUI : IReactiveObserver
{
    // ObserveChanges() 由外部管理器调用，不在类内部调用
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

生成的属性名会去掉 `m_` 前缀并将首字母大写（`m_Health` → `Health`，`m_playerName` → `PlayerName`）。

### 生成的代码

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
        get => m_Health;
        set
        {
            if (value != m_Health)
            {
                m_Health = value;
                __IncrementVersion();
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
                __IncrementVersion();
            }
        }
    }
    // ...
}
```

### 自定义属性特性

使用 `[VersionFieldProperty]` 为生成的属性添加自定义特性。支持两种构造方式：

- `VersionFieldProperty(Type type)` — 用于无参特性，自动补齐完整命名空间
- `VersionFieldProperty(string text)` — 用于带参特性，字符串原样输出

```csharp
public partial class PlayerData : IVersion
{
    [VersionField]
    [VersionFieldProperty(typeof(JsonIgnoreAttribute))]
    private int m_Health;

    [VersionField]
    [VersionFieldProperty("System.Obsolete(\"Use NewName\")")]
    private string m_Name;

    [VersionField]
    [VersionFieldProperty(typeof(JsonIgnoreAttribute))]
    [VersionFieldProperty("System.Obsolete(\"Use NewSpeed\")")]
    private float m_Speed;
}
```

生成的代码：

```csharp
[Newtonsoft.Json.JsonIgnoreAttribute]
public int Health { get => m_Health; set { ... } }

[System.Obsolete("Use NewName")]
public string Name { get => m_Name; set { ... } }

[Newtonsoft.Json.JsonIgnoreAttribute]
[System.Obsolete("Use NewSpeed")]
public float Speed { get => m_Speed; set { ... } }
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
            if (m_Player != null) m_Player.__Parent = null;  // 清除旧的父级
            m_Player = value;
            if (value != null) value.__Parent = this;        // 设置新的父级
            __IncrementVersion();
        }
    }
}
```

### 版本传播

版本变化会向上传播到整个父级链：

```
GameData (__Parent=null)
  └── PlayerData (__Parent=GameData)
        └── WeaponData (__Parent=PlayerData)

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

game.MainCharacter = player;        // player.__Parent = game
player.Skills.Add(skill);           // skill.__Parent = player.Skills, Skills.__Parent = player

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

## 数据同步

把 `[VersionField]` 类声明为 `: IVersionSync`,即可让对象树可同步。同步是**类级别开关**——`IVersionSync` 类里的每个 `[VersionField]` 都同步(没有逐字段属性);只声明 `: IVersion` 的类仅做版本追踪。同步采用**扁平注册表 + 直接写入**:一个 `SyncContext` 用 `Dictionary<int, 节点>` 以稳定 id 持有所有可同步节点,并持有改动写入的流——任何改动在发生的那一刻就把自己的记录直接写进流,一个 `Commit()` 取出自上次调用以来写进流的全部内容(首次 commit = 全量,之后 = 增量)。

```csharp
public partial class PlayerData : IVersionSync   // 下面每个 [VersionField] 都同步
{
    [VersionField] private int m_Health;
    [VersionField] private string m_Name;
}
```

### SyncContext

`SyncContext` 是一个轻量注册表内核——暴露的状态 + 两个操作;用 `root.AttachTo(ctx)` 播种根:

```csharp
public class SyncContext
{
    public readonly Dictionary<int, IVersionSync> __Objects;  // 注册表:id -> 节点(生成代码内联驱动)
    public int __NextId;                                      // id 分配器(root 拿 1)
    public BinaryWriter __Writer { get; }                   // 改动写入的记录 buffer

    public MemoryStream Commit();         // 交出 writer 自己的流,定位在自上次 commit 以来写入的记录处
    public void Apply(BinaryReader r);    // 从 reader 当前位置读到流末尾,套用(全量或增量)
}
```

### 用法

```csharp
// 生产端:建上下文并播种根
var producerCtx = new SyncContext();
var producer = new PlayerData();
producer.AttachTo(producerCtx);
producer.Health = 100;

// 首次 commit:交出 writer 的流,定位在至此写入的全部记录处 = 全量状态
var full = producerCtx.Commit();

// 消费端:播种同一个根(两端都分到 id 1),再套用
var consumerCtx = new SyncContext();
var consumer = new PlayerData();
consumer.AttachTo(consumerCtx);
consumerCtx.Apply(new BinaryReader(full));   // 从 full.Position 读到流末尾;实际场景里你会把这些字节经传输层发出去

// 增量:改数据,下一次 Commit 只交出自上次以来写进流的内容
producer.Health = 80;            // 标量改动,当场写进流
producer.Items[0].Count = 3;     // 元素按自身 id 自报(无需遍历树)
var delta = producerCtx.Commit();
consumerCtx.Apply(new BinaryReader(delta));  // 套到现有状态上
```

`Apply` 静默套用(不会回写)。`SyncContext` 自己持有 `__Writer`/流,你只跟 `byte[]` 打交道——自行传输或落盘。

### 模型

- **扁平注册表 + 直接写入,而非对比**:每个节点有稳定 `__SyncId`。setter 只把**自己这个节点**的改动当场写进 `ctx.__Writer`——没有脏集合,不从根遍历。线格式是一串自描述记录,读到流尾为止:`[0][id][数据]` 是节点记录(一个字段,或一条容器 op),`[1][id]` 是删除记录。同一字段改 N 次就落 N 条记录,消费端顺序套用、最后一条生效。
- **引用而非递归**:对象/容器字段序列化为被引用节点的 `__SyncId`(0 表示 null)。消费端在「第一次读到某引用」时,在节点自己的 `__Apply` 里(用 `ctx.__Objects`)按字段**静态类型**创建该节点——线上无类型标签。节点 id 按 pre-order 分配(父 < 子孙),保证父节点的引用记录总是先于被引用节点自己的记录被读到。
- **生命周期**:重指或置空引用会反注册旧子树(每个节点落一条删除记录),并注册 + 写出新子树。嵌套对象的内部字段改动以该对象自己的记录同步,与父节点无关。
- **集合**:`VersionList`/`VersionDictionary`/`VersionHashSet` 都是注册表节点;每条结构 op(insert/removeAt/set/clear、add/remove、set/remove)当场写出记录,批量操作整表重发。对于元素为 `IVersionSync` 对象的 `VersionList`,每个元素是独立的注册表节点——内部字段变化是该元素自己的记录(列表 op 只携带元素 id),而非整元素重发。

### 支持的字段类型

- 标量:`bool/byte/sbyte/short/ushort/int/uint/long/ulong/float/double/char/decimal/string/enum`
- 嵌套的**具体** `IVersionSync` 类型
- `VersionList<T>`,`T` 为标量或具体 `IVersionSync` 类型(对象元素作为独立节点同步)
- `VersionDictionary<K,V>`、`VersionHashSet<T>`,键/值/元素为标量类型

### 限制

- 每个类最多 64 个同步的 `[VersionField]` 成员。
- 两端必须在首次 `Apply` 前用 `root.AttachTo(ctx)` 播种同一个根(两端都确定性分到 id 1)。
- SyncObject/容器成员必须是可 `new T()` 的具体类型;接口/抽象/多态不支持。
- `VersionDictionary` 的对象值、`VersionHashSet` 的对象元素不支持(VS2001);`VersionHashSet` 仅同步增删。
- `VersionDictionary` 的对象**值**、`VersionHashSet` 的对象**元素**暂不支持字段级同步。

## 版本容器

ReactiveBinding 提供基于版本号的容器，用于高效的集合变更检测。只比较版本号，而不是比较集合内容。

### 可用容器

- `VersionList<T>` - 实现 `IList<T>, IVersion`
- `VersionDictionary<K,V>` - 实现 `IDictionary<K,V>, IVersion`
- `VersionHashSet<T>` - 实现 `ISet<T>, IVersion`

每次修改操作（Add、Remove、Clear 等）都会递增 `__Version` 属性。

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

    public void ResetChanges()
    {
        __reactive_initialized = false;
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

## 继承

派生类可以添加自己的响应式成员。每个类处理自己的 `[ReactiveSource]` 和 `[ReactiveBind]`，生成的代码通过 `base.ObserveChanges()` 自动链式调用。

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

为 `DerivedUI` 生成的代码：

```csharp
partial class DerivedUI
{
    private bool __reactive_initialized;
    private int __reactive_Mana = default!;

    public override void ObserveChanges()
    {
        base.ObserveChanges();  // 处理 Health 变更检测

        if (!__reactive_initialized)
        {
            __reactive_initialized = true;
            __reactive_Mana = Mana;
            OnManaChanged(Mana);
            return;
        }
        // Mana 变更检测...
    }

    public override void ResetChanges()
    {
        base.ResetChanges();
        __reactive_initialized = false;
    }
}
```

- 只有 `[ReactiveBind]` 才会触发派生类的代码生成；仅有 `[ReactiveSource]` 不会
- `virtual` 仅在同一编译中有派生类需要 `override` 时才添加
- 没有 `[ReactiveBind]` 的派生类跳过代码生成（直接继承基类）
- 每个类只处理自己的 `[ReactiveSource]` 和 `[ReactiveBind]` 成员
- 所有 `IReactiveObserver` 类禁止手动实现 `ObserveChanges()`/`ResetChanges()`（RB1005/RB1006）

## IReactiveObserver 接口

使用 `[ReactiveBind]` 的类必须实现 `IReactiveObserver`。Source Generator 会自动实现 `ObserveChanges()` 和 `ResetChanges()`。

```csharp
public interface IReactiveObserver
{
    void ObserveChanges();
    void ResetChanges();
}
```

- `ObserveChanges()` - 检查数据变更并触发绑定的回调。首次调用（或重置后），所有回调会以 default 作为旧值触发。
- `ResetChanges()` - 重置响应式状态，使下一次 `ObserveChanges()` 调用表现为首次调用。适用于对象池/复用场景。

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
| RB0003 | 错误 | 类内未调用 ObserveChanges()，可用 [ReactiveObserveIgnore] 忽略 |
| RB1001 | 错误 | 类必须是 partial |
| RB1002 | 错误 | 类必须实现 IReactiveObserver |
| RB1003 | 错误 | ReactiveThrottle 值必须 >= 1 |
| RB1004 | 错误 | ReactiveThrottle 需要实现 IReactiveObserver |
| RB1005 | 错误 | 不允许手动实现 ObserveChanges() |
| RB1006 | 错误 | 不允许手动实现 ResetChanges() |
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
| RB3010 | 错误 | 引用的成员存在但未标记 [ReactiveSource] |
| VF1001 | 错误 | VersionField 类必须是 partial |
| VF1002 | 错误 | VersionField 类必须实现 IVersion |
| VF2001 | 错误 | VersionField 必须有 m_ 前缀 |
| VF2002 | 错误 | VersionField 必须是 private |
| VF2003 | 错误 | 属性名已存在 |
| VF3001 | 错误 | __Parent 属性只能在 IVersion 实现内部访问 |
| VF3002 | 错误 | 不允许直接访问 VersionField 的backing字段 |
| VF3003 | 错误 | VersionField 不允许设置默认值 |
| VS2001 | 错误 | 不支持的同步字段类型(IVersionSync 类里的 [VersionField]) |
