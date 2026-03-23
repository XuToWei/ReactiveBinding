# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ReactiveBinding is a C# Source Generator that provides compile-time reactive data binding for Unity. It generates change detection code at compile time, eliminating runtime reflection overhead.

## Build Commands

```bash
dotnet build Generator~/Core/ReactiveBinding.Generator.csproj
dotnet test Generator~/Tests/ReactiveBinding.Generator.Tests.csproj
dotnet test Generator~/Tests/ReactiveBinding.Generator.Tests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

The generator DLL is automatically copied to `Runtime/Plugins/` after build via a PostBuild target.

## Architecture

### Directory Structure
- `Runtime/` - Attributes, interfaces (`IReactiveObserver`, `IVersion`), version containers (`VersionList<T>`, `VersionDictionary<K,V>`, `VersionHashSet<T>`)
- `Generator~/Core/` - Generator implementation (tilde suffix excludes from Unity compilation)
- `Generator~/Tests/` - NUnit tests

### Core Components

**Generators** (`ISourceGenerator`):
- **ReactiveBindGenerator** - Generates `ObserveChanges()` and `ResetChanges()` from `[ReactiveSource]`/`[ReactiveBind]`
- **VersionFieldGenerator** - Generates properties from `[VersionField]` fields with `IVersion` implementation

**Syntax Receivers** (`ISyntaxContextReceiver`):
- **ReactiveSyntaxReceiver** - Collects `[ReactiveSource]`/`[ReactiveBind]`/`[ReactiveThrottle]`, builds `ReactiveClassData`
- **VersionFieldSyntaxReceiver** - Collects `[VersionField]` fields

**Analyzers** (`DiagnosticAnalyzer`):
- **ReservedMethodAnalyzer** - Prevents manual `ObserveChanges()`/`ResetChanges()` (RB1005/RB1006)
- **ObserveChangesCallAnalyzer** - Warns when `ObserveChanges()` not called in class (RB0003), ignored by `[ReactiveObserveIgnore]` or reactive base class
- **ParentAccessAnalyzer** - Prevents `IVersion.Parent` access outside `IVersion` implementations (VF3001)
- **VersionFieldAccessAnalyzer** - Prevents direct access to `[VersionField]` backing fields (VF3002)

**Helpers**: `MethodBodyAnalyzer` (auto-inference), `ReactiveDataModels`, `DiagnosticDescriptors`

### Code Generation Flow

1. `ReactiveSyntaxReceiver` collects `[ReactiveSource]` members and `[ReactiveBind]` methods
2. For parameterless `[ReactiveBind]` (auto-inference), `MethodBodyAnalyzer` finds referenced sources
3. Generator validates: partial class, implements `IReactiveObserver`, source/binding constraints
4. Generates partial class with:
   - Cache fields (`__reactive_{name}`), `__reactive_initialized` flag, optional `__reactive_callCount`
   - `ObserveChanges()`: plain / `virtual` (when derived classes override) / `override` (with `base.ObserveChanges()`)
   - `ResetChanges()`: same modifier pattern as `ObserveChanges()`
   - Derived classes without `[ReactiveBind]` skip generation (inherit from base)

### Auto-Inference Binding

When `[ReactiveBind]` has no parameters, `MethodBodyAnalyzer.FindReferencedSources()` analyzes the method body:
- Detects direct access (`Health`), this access (`this.Health`), method calls (`GetDamage()`)
- Handles local variable shadowing (shadowed names ignored)
- Must have no parameters (RB3009), must find sources (RB3008)

### VersionField Generator

Generates properties from `[VersionField]` fields (`m_Health` → `Health` property):
- Field: must be `private`, must have `m_` prefix
- Class: must be `partial`, must implement `IVersion`
- Generates: `__version`, `Parent`, `Version`, `IncrementVersion()`
- Nested `IVersion` fields: auto-manages parent chain
- Version propagation: changes bubble up through parent chain via `IncrementVersion()`
- Float/double: epsilon comparison (1e-6f / 1e-9d)
- `Parent` only accessible within `IVersion` implementations (VF3001)
- Backing fields (`m_` prefixed) must use generated properties (VF3002)

### Diagnostics

**RB0xxx** (warnings): RB0001 unmatched source | RB0003 ObserveChanges() not called
**RB1xxx** (class): RB1001 not partial | RB1002 no IReactiveObserver | RB1003 throttle < 1 | RB1004 throttle without interface | RB1005 manual ObserveChanges | RB1006 manual ResetChanges
**RB2xxx** (source): RB2001 void method | RB2002 no getter | RB2003 has params | RB2004 unsupported type | RB2005 no equality op
**RB3xxx** (bind): RB3001 no ids | RB3002 static | RB3003 not void | RB3004 param count | RB3005 type mismatch | RB3006 duplicate | RB3007 no nameof | RB3008 no sources inferred | RB3009 auto-infer with params | RB3010 not marked source
**VF1xxx** (class): VF1001 not partial | VF1002 no IVersion
**VF2xxx** (field): VF2001 no m_ prefix | VF2002 not private | VF2003 property exists
**VF3xxx** (usage): VF3001 Parent access | VF3002 direct field access

### Testing

`GeneratorTestHelper` provides: `RunGenerator()`, `RunVersionFieldGenerator()`, `RunReservedMethodAnalyzer()`, `RunObserveChangesCallAnalyzer()`, `RunParentAccessAnalyzer()`, `RunVersionFieldAccessAnalyzer()`

Assertions: `AssertNoErrors()`, `AssertHasDiagnostic(id)`, `AssertGeneratedContains(text)`
