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
            OnHealthChanged(__reactive_Health, __reactive_Health);
            OnStatsChanged(__reactive_Health, __reactive_GetTotalDamage);
            OnCombatStatsChanged();  // 自动推断绑定
            return;
        }

        bool __changed_Health = false;
        bool __changed_GetTotalDamage = false;
        int __old_Health = __reactive_Health;
        int __old_GetTotalDamage = __reactive_GetTotalDamage;

        int __current_Health = Health;
        if (__current_Health != __reactive_Health)
        {
            __changed_Health = true;
            __reactive_Health = __current_Health;
            OnHealthChanged(__old_Health, __reactive_Health);
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
- **响应式继承** - 响应式派生类可添加自己的绑定并自动链式调用基类（`VersionField` 明确不支持继承）
- **节流控制** - 控制观察频率
- **版本容器** - VersionList、VersionDictionary、VersionHashSet，基于版本号的高效变更检测
- **VersionField 自动生成** - 从私有字段自动生成属性，支持版本追踪和父级链传播
- **数据同步** - 类声明 `: IVersionSync` 即同步其所有 `[VersionField]`；`SyncContext` 扁平注册表序列化进调用方持有的 `BinaryWriter`——全量快照(`CaptureFull`)或合并增量(`CaptureDelta`)
- **自定义属性特性** - `[VersionFieldProperty]` 为生成的属性添加自定义特性（支持 `Type` 和 `string` 两种方式）
- **完整诊断** - 编译时错误/警告代码

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
2. **快速失败** - 编译时诊断在运行前捕获错误，AI 获得即时反馈
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

当类内部没有调用 `ObserveChanges()` 时，忽略 RB10009 警告。适用于 `ObserveChanges()` 由外部调用的场景（如管理器或框架统一调用）。

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
    [VersionField] private int __Health;
    [VersionField] private float __Speed;
    [VersionField] private string __Name;
}
```

生成的属性名会去掉 `__` 前缀并将首字母大写（`__Health` → `Health`，`__playerName` → `PlayerName`）。

### 生成的代码

```csharp
partial class PlayerData
{
    public ReactiveBinding.IVersion __Parent { get; set; }
    public int __Version { get; set; }

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

所有以 `__` 开头的成员都是生成代码与 ReactiveBinding 运行时之间的内部协议；业务代码不能读取、写入、
调用或捕获这些成员（VF10012）。读取当前版本请使用 `Version`，复用子树请使用
`Reset()`。`IVersionSync` 还提供只读的 `SyncId`、`SyncContext` 和 `IsDirty`；独立的 `VersionSync*`
容器通过 `InitSync(...)` 完成配置。

### 自定义属性特性

使用 `[VersionFieldProperty]` 为生成的属性添加自定义特性。支持两种构造方式：

- `VersionFieldProperty(Type type)` — 用于无参特性，自动补齐完整命名空间
- `VersionFieldProperty(string text)` — 用于带参特性，字符串原样输出

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

生成的代码：

```csharp
[Newtonsoft.Json.JsonIgnoreAttribute]
public int Health { get => __Health; set { ... } }

[System.Obsolete("Use NewName")]
public string Name { get => __Name; set { ... } }

[Newtonsoft.Json.JsonIgnoreAttribute]
[System.Obsolete("Use NewSpeed")]
public float Speed { get => __Speed; set { ... } }
```

### 嵌套 IVersion 字段

当字段类型实现了 `IVersion` 时，生成器会自动管理父级链：

```csharp
public partial class GameData : IVersion
{
    [VersionField] private PlayerData __Player;  // PlayerData : IVersion
}

// 生成的 setter：
public PlayerData Player
{
    get => __Player;
    set
    {
        if (value != __Player)
        {
            if (__Player != null) __Player.__Parent = null;  // 清除旧的父级
            __Player = value;
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
    [VersionField] private VersionList<ItemData> __Items;
    [VersionField] private int __Gold;
}

public partial class TeamData : IVersion
{
    [VersionField] private VersionDictionary<string, PlayerData> __Players;
}
```

### 复杂层级示例

完整的 3 层嵌套和容器示例：

```csharp
// 第 3 层 - 叶子节点
public partial class SkillData : IVersion
{
    [VersionField] private int __Damage;
    [VersionField] private float __CoolDown;
}

// 第 2 层 - 中间层（带容器）
public partial class CharacterData : IVersion
{
    [VersionField] private int __Health;
    [VersionField] private VersionList<SkillData> __Skills;
}

// 第 1 层 - 根节点（同时有单个字段和容器）
public partial class GameData : IVersion
{
    [VersionField] private CharacterData __MainCharacter;
    [VersionField] private VersionList<CharacterData> __AllCharacters;
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
3. 字段必须有 `__` 前缀
4. 字段必须是 `private`
5. 带 `[VersionField]` 的类不能继承另一个 `IVersion`/`IVersionSync` 实现（VF10003）
6. 同一个 `IVersion` 实例只能占用一个生成属性或容器槽；重复归属会抛出 `InvalidOperationException`

嵌套类型的所有外层类型也必须是 `partial`，生成器才能安全补充 partial 声明。

## 数据同步

把 `[VersionField]` 类声明为 `: IVersionSync`,即可让对象树可同步。同步是**类级别开关**——`IVersionSync` 类里的每个 `[VersionField]` 都同步(没有逐字段属性);只声明 `: IVersion` 的类仅做版本追踪。同步采用**扁平注册表 + 全量快照,可选合并增量**:一个 `SyncContext` 用 `Dictionary<int, 节点>` 以稳定 id 持有所有可同步节点。**调用方拥有输出流**——`CaptureFull(writer)` 把整个注册表写进一个 `BinaryWriter`,是一份完整、自包含的快照(keyframe);打完基线后,`CaptureDelta(writer)` 只写从 clean 变 dirty 时登记的节点 id,并附带已移除子树的 tombstone。`Apply(reader)` 每次精确消费一个自定界帧并把消费端重建成一致。

```csharp
public partial class PlayerData : IVersionSync   // 下面每个 [VersionField] 都同步
{
    [VersionField] private int __Health;
    [VersionField] private string __Name;
}
```

### SyncContext

`SyncContext` 是注册表内核;用 `root.AttachTo(ctx)` 播种根:

```csharp
public class SyncContext
{
    public readonly Dictionary<int, IVersionSync> __Objects;  // 注册表:id -> 节点(生成代码内联驱动)
    public int __NextId;                                      // id 分配器(root 拿 1)

    public void CaptureFull(BinaryWriter w);   // 把整个注册表写成全量快照(keyframe),并清脏
    public void CaptureDelta(BinaryWriter w);  // 只写自上次 capture 以来变化的节点(增量)
    public void Apply(BinaryReader r);         // 从 reader 当前位置精确套用一个自定界帧
    public void Compact();                     // 在维护时机释放注册表的多余容量
    public void TrimScratch();                 // 释放 capture/apply 复用暂存集合的多余容量
}
```

`Compact` 和 `TrimScratch` 不会改变 id 或待发送帧状态。它们通过重建相关 Dictionary、Set、List 来释放容量，因此在没有 `TrimExcess` 的旧 Unity/.NET profile 上也有效；只应在大负载峰值后的场景切换等低频时机调用。`Compact` 会替换公开的 `__Objects` Dictionary 实例，不要跨调用保留该 Dictionary 的别名引用。

### 用法

```csharp
// 生产端:建上下文并播种根
var producerCtx = new SyncContext();
var producer = new PlayerData();
producer.AttachTo(producerCtx);
producer.Health = 100;

// CaptureFull 把整个注册表写进调用方持有的 writer = 全量快照;取它的字节
var ms = new MemoryStream();
producerCtx.CaptureFull(new BinaryWriter(ms));
byte[] payload = ms.ToArray();   // 实际场景里你会把这些字节经传输层发出去

// 消费端:播种同一个根(两端都分到 id 1),再套用
var consumerCtx = new SyncContext();
var consumer = new PlayerData();
consumer.AttachTo(consumerCtx);
consumerCtx.Apply(new BinaryReader(new MemoryStream(payload)));

// 之后:改数据,再发一份增量(只含变化的节点),套到现有消费端状态上
producer.Health = 80;
var delta = new MemoryStream();
producerCtx.CaptureDelta(new BinaryWriter(delta));
consumerCtx.Apply(new BinaryReader(new MemoryStream(delta.ToArray())));
```

`Apply` 会原地更新已有节点（保持对象引用不变）、首次见到引用时创建节点，并在帧结束时给所有 touched 同步节点及其同步祖先赋同一个新版本号。每个受影响节点每帧最多更新一次，因此 `ReactiveBind` 能观察到变化，又不会反复沿 parent chain 传播。Apply **不会**把出站同步状态标脏，所以不会形成回写循环。`CaptureFull` 写完整状态并清理未提及的消费端节点；`CaptureDelta` 只写登记过的 dirty 节点，并通过帧尾 tombstone 立即删除生产端已经移除的消费端子树。

### 模型

- **扁平注册表 + 全量快照/增量**:每个节点有稳定 `__SyncId`。帧格式为 `[byte isFull][正数 varuint 节点 id + 数据 ...][varuint 0][varuint tombstoneCount][varuint tombstone ids ...]`；零 id 终止节点记录，因此同一流里可以连续放多帧。全量 capture 按 id 升序访问所有活动节点(父 < 子孙)；增量 capture 只排序并访问 clean→dirty 时登记的 id，使空帧和少量变化帧的成本取决于 dirty 数量而不是注册表大小。
- **紧凑元数据**:节点/引用 id、集合 count、List index、opCount 和 tombstone id 都是非负 `int`，使用 7-bit continuation 编码；零仍表示 null 引用或节点记录结束。`SyncContext` 只分配 `1..int.MaxValue - 1`，因此 id 不会溢出为负数。字段 mask 和标量字段内容仍保持各自类型的固定宽度编码。
- **引用而非递归**:对象/容器字段序列化为被引用节点的 varuint `__SyncId`(0 表示 null)。消费端第一次读到某引用时,在节点自己的 `__Apply` 里(用 `ctx.__Objects`)按字段**静态类型**创建该节点——wire 上没有类型标签。节点 id 按 pre-order 分配,所以升序 capture 会先写父节点的引用记录,再写被引用节点自己的记录。
- **删除和版本以帧为单位**:delta tombstone 位于正常记录之后,保证父引用或集合操作先套用,再 reset 并删除旧消费端子树；全量快照还会清理未提及的节点。完整帧套用后,所有 touched 同步节点及同步祖先各自只更新一次,并得到同一个新版本号；已套用节点保持 clean，不会产生回写 delta。
- **集合**:被同步的 `[VersionField]` 容器必须是 `VersionSyncList`/`VersionSyncDictionary`/`VersionSyncHashSet`(版本-only 的 `VersionList`/等不可同步 → VS10001)。它们都是注册表节点,按完整内容或逐帧 op 日志序列化。List 批量修改使用 range opcode；Dictionary 同一 key 的写入合并为最后一次操作；List 对同一稳定 index 的相邻写入合并为最终值；HashSet 批量修改记录 add/remove 差异。记录器按预计编码字节数比较 delta 与 full，并在完整记录更小时自动回退。对象容器(`VersionSyncList<T>`、`VersionSyncDictionary<K,V>` 的值、或 `VersionSyncHashSet<T>` 的元素,其中对象类型实现 `IVersionSync`)中的每个对象都是按 id 引用的独立注册表节点,各自同步自己的字段。

### 支持的字段类型

- 标量:`bool/byte/sbyte/short/ushort/int/uint/long/ulong/float/double/char/decimal/string/enum`
- 嵌套的**具体** `IVersionSync` 类型
- `VersionSyncList<T>`,`T` 为标量或具体 `IVersionSync` 类型(对象元素作为独立节点同步)
- `VersionSyncDictionary<K,V>`,`K` 为标量,`V` 为标量或具体 `IVersionSync` 类型(对象值作为独立节点同步)
- `VersionSyncHashSet<T>`,`T` 为标量或具体 `IVersionSync` 类型(对象元素作为独立节点同步)

### 限制

- 两端必须在首次 `Apply` 前用 `root.AttachTo(ctx)` 播种同一个根(两端都确定性分到 id 1)。
- 同步采用 single-writer：两帧之间只有生产端可以创建/移除同步节点。若两端独立分配节点，各自 context 的 id 可能冲突；需要双向修改对象图时，应使用分开的权威数据流，或先在应用层设计 writer/id 命名空间。
- 不支持 `VersionField`/`IVersionSync` 继承（VF10003）；请通过字段或容器组合版本节点。
- 同一个 `IVersion`/`IVersionSync` 实例只能出现在一个字段或容器槽；从旧 owner 移除或 Reset 后才能复用。
- SyncObject/容器成员必须是可 `new T()` 的具体类型;接口/抽象/多态会报 VS10003。
- `VersionSyncDictionary` 的对象**键**不支持(VS10004);键必须是标量。
- `VersionSyncDictionary` 只支持默认 equality comparer；自定义 comparer 不会编码进 wire，因此会被拒绝。
- `VersionHashSet<T>` 和 `VersionSyncHashSet<T>` 均不提供自定义 comparer 构造函数，并统一使用 `EqualityComparer<T>.Default`。元素存放期间不得修改影响 `Equals`/`GetHashCode` 的字段。同步对象元素通常应保持引用相等，因为消费端会先插入被引用节点，再应用该节点的字段记录。
- 同步容器若直接作为根使用，必须在 `AttachTo` 前调用对应 `InitSync` 注入序列化器/工厂；由生成的 `[VersionField]` 属性持有时会自动完成。

## 版本容器

ReactiveBinding 提供基于版本号的容器，用于高效的集合变更检测。只比较版本号，而不是比较集合内容。

### 可用容器

- `VersionList<T>` - 实现 `IList<T>, IVersion`
- `VersionDictionary<K,V>` - 实现 `IDictionary<K,V>, IVersion`
- `VersionHashSet<T>` - 实现 `ISet<T>, IVersion`

每次修改操作（Add、Remove、Clear 等）都会递增公开的 `Version` 属性。
`VersionHashSet<T>` 不接受自定义 comparer，始终使用 `EqualityComparer<T>.Default`。与 `HashSet<T>` 一样，元素存放期间必须保持参与 `Equals`/`GetHashCode` 的字段稳定。
`VersionList<T>` 和 `VersionSyncList<T>` 还提供 `SortIfNeeded(...)`：它先线性检查是否已有序，只有顺序确实需要变化时才排序、递增版本并记录同步状态。

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
            __reactive_Items_version = Items?.__Version ?? -1;
            OnItemsChanged();
            OnItemsChangedWithParam(Items);
            return;
        }

        var __current_Items_version = Items?.__Version ?? -1;
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
            OnManaChanged(__reactive_Mana);
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
- 每个非 sealed 的响应式根类都会生成 `virtual` 方法，因此跨程序集/Unity asmdef 的派生绑定也能正常 override
- 没有 `[ReactiveBind]` 的派生类跳过代码生成（直接继承基类）
- 每个类只处理自己的 `[ReactiveSource]` 和 `[ReactiveBind]` 成员
- 所有 `IReactiveObserver` 类禁止手动实现 `ObserveChanges()`/`ResetChanges()`（RB10005/RB10006）

## IReactiveObserver 接口

使用 `[ReactiveBind]` 的类必须实现 `IReactiveObserver`。Source Generator 会自动实现 `ObserveChanges()` 和 `ResetChanges()`。

```csharp
public interface IReactiveObserver
{
    void ObserveChanges();
    void ResetChanges();
}
```

- `ObserveChanges()` - 检查数据变更并触发绑定的回调。首次调用（或重置后），oldValue 和 newValue 都是当前值。
- `ResetChanges()` - 重置响应式状态，使下一次 `ObserveChanges()` 调用表现为首次调用。适用于对象池/复用场景。

## 使用要求

1. 类必须声明为 `partial`
2. 类必须实现 `IReactiveObserver`
3. `[ReactiveBind]` 显式指定数据源时必须使用 `nameof()` 表达式（或使用无参数的自动推断模式）
4. `[ReactiveSource]` 方法必须有返回值且无参数
5. `[ReactiveSource]` 属性必须有 getter
6. 自定义 struct 类型必须实现 `==` 和 `!=` 运算符

嵌套响应式类的所有外层类型也必须声明为 `partial`。

## 编译器诊断

| 代码 | 类型 | 描述 |
|------|------|-------------|
| RB10001 | 错误 | 类必须是 partial |
| RB10002 | 错误 | 类必须实现 IReactiveObserver |
| RB10003 | 错误 | ReactiveThrottle 值必须 >= 1 |
| RB10004 | 错误 | ReactiveThrottle 需要实现 IReactiveObserver |
| RB10005 | 错误 | 不允许手动实现 ObserveChanges() |
| RB10006 | 错误 | 不允许手动实现 ResetChanges() |
| RB10007 | 警告 | ReactiveSource 没有对应的 ReactiveBind |
| RB10008 | 错误 | ReactiveBind 引用了不存在的数据源 |
| RB10009 | 警告 | 类内未调用 ObserveChanges()，可用 [ReactiveObserveIgnore] 忽略 |
| RB10010 | 错误 | ReactiveSource 方法返回 void |
| RB10011 | 错误 | ReactiveSource 属性没有 getter |
| RB10012 | 错误 | ReactiveSource 方法有参数 |
| RB10013 | 错误 | 不支持的 ReactiveSource 类型 |
| RB10014 | 错误 | 结构体缺少相等运算符 |
| RB10015 | 错误 | ReactiveSource 标识重复 |
| RB10016 | 错误 | ReactiveBind 没有指定数据源 |
| RB10017 | 错误 | ReactiveBind 方法是静态的 |
| RB10018 | 错误 | ReactiveBind 方法返回值不是 void |
| RB10019 | 错误 | 参数数量无效 |
| RB10020 | 错误 | 参数类型不匹配 |
| RB10021 | 错误 | 重复的数据源标识 |
| RB10022 | 错误 | 未使用 nameof() |
| RB10023 | 错误 | 自动推断未在方法体中找到数据源 |
| RB10024 | 错误 | 自动推断的方法不能有参数 |
| RB10025 | 错误 | 引用的成员存在但未标记 [ReactiveSource] |
| RB10026 | 错误 | ReactiveBind 回调是泛型或使用 ref/out/in 参数 |
| VF10001 | 错误 | VersionField 类必须是 partial |
| VF10002 | 错误 | VersionField 类必须实现 IVersion |
| VF10003 | 错误 | 不支持 VersionField/IVersionSync 继承 |
| VF10004 | 错误 | 用户成员与 VersionField 保留的生成状态冲突 |
| VF10005 | 错误 | VersionField 必须有 __ 前缀 |
| VF10006 | 错误 | VersionField 必须是 private |
| VF10007 | 错误 | 属性名已存在 |
| VF10008 | 错误 | VersionField 不能是 static、readonly 或 const |
| VF10009 | 错误 | VersionField 生成了无效的属性标识符 |
| VF10010 | 错误 | 不允许直接访问 VersionField 的backing字段 |
| VF10011 | 错误 | VersionField 不允许设置默认值 |
| VF10012 | 错误 | 禁止直接访问 `IVersion`/`IVersionSync` 的内部 `__*` 成员 |
| VS10001 | 错误 | 不支持的同步字段类型(IVersionSync 类里的 [VersionField]) |
| VS10002 | 错误 | 同步对象类型必须有 public 无参构造函数 |
| VS10003 | 错误 | 同步对象/接口类型必须是具体、非抽象的 IVersionSync 类 |
| VS10004 | 错误 | VersionSyncDictionary 的键类型必须是标量 |
