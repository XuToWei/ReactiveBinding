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

## 特性

- **编译时代码生成** - 零运行时反射开销
- **多种数据源类型** - 支持字段、属性和方法
- **灵活的回调签名** - 支持 0、N 或 2N 个参数
- **多数据源绑定** - 多个数据源绑定到一个回调
- **首次调用初始化** - 自动触发初始回调
- **节流控制** - 控制观察频率
- **完整诊断** - 17 个编译时错误/警告代码

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

### ReactiveThrottleAttribute

控制 `ObserveChanges()` 实际执行检查的频率。

```csharp
[ReactiveThrottle(10)]  // 每 10 次调用才执行一次检查
public partial class PlayerUI : IReactiveObserver
{
    // ...
}
```

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
3. `[ReactiveBind]` 必须使用 `nameof()` 表达式
4. `[ReactiveSource]` 方法必须有返回值且无参数
5. `[ReactiveSource]` 属性必须有 getter

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
| RB3001 | 错误 | ReactiveBind 没有指定数据源 |
| RB3002 | 错误 | ReactiveBind 方法是静态的 |
| RB3003 | 错误 | ReactiveBind 方法返回值不是 void |
| RB3004 | 错误 | 参数数量无效 |
| RB3005 | 错误 | 参数类型不匹配 |
| RB3006 | 错误 | 重复的数据源标识 |
| RB3007 | 错误 | 未使用 nameof() |
