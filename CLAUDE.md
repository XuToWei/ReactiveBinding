# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ReactiveBinding is a C# Source Generator that provides compile-time reactive data binding for Unity. It generates change detection code at compile time, eliminating runtime reflection overhead.

### Usage Pattern

1. Class implements `IReactiveObserver`, marked `partial`
2. `[ReactiveSource]` marks data sources (fields, properties, methods)
3. `[ReactiveBind(nameof(Source))]` marks callbacks, or `[ReactiveBind]` for auto-inference
4. Call `ObserveChanges()` each frame (e.g., in `Update()`) to detect changes and trigger callbacks
5. Optional: `[ReactiveThrottle(N)]` controls check frequency, `[ReactiveObserveIgnore]` ignores call check, `ResetChanges()` for object pooling

### Key Features

- **Zero reflection** - All code generated at compile time
- **Polling model** - No event subscription/unsubscription, just call `ObserveChanges()`
- **Auto-inference** - `[ReactiveBind]` without parameters auto-detects referenced sources from method body
- **Flexible callbacks** - 0, N, or 2N parameters (old/new value pairs)
- **Inheritance** - Derived classes chain via `base.ObserveChanges()` automatically
- **VersionField** - `[VersionField]` auto-generates properties with version tracking and parent chain propagation
- **Custom property attributes** - `[VersionFieldProperty]` adds custom attributes to generated properties (Type for parameterless, string for parameterized)
- **Version containers** - `VersionList<T>`, `VersionDictionary<K,V>`, `VersionHashSet<T>` for efficient collection change detection
- **Data synchronization** - `[VersionSync]` on `[VersionField]` fields generates `IVersionSyncable` (`Commit`/`GetFull`/`GetDelta`/`Apply`/`ApplyDelta`) for full + field-level incremental `byte[]` sync
- **35 diagnostics** - Compile-time error/warning codes catch mistakes early

## Build Commands

```bash
dotnet build NuGet/ReactiveBinding.Generator/ReactiveBinding.Generator.csproj
dotnet test NuGet/ReactiveBinding.Tests/ReactiveBinding.Generator.Tests.csproj
dotnet test NuGet/ReactiveBinding.Tests/ReactiveBinding.Generator.Tests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

The generator DLL is automatically copied to `Unity/Runtime/Plugins/` after build via a PostBuild target.

## Repo layout

The repo splits into two parallel top-level directories:
- `Unity/` - the Unity (UPM) package (`package.json`, `Runtime/` + `.meta`, `Samples~/`). Install via `...ReactiveBinding.git?path=Unity`.
- `NuGet/` - the .NET side: `ReactiveBinding.Generator/` (generator), `ReactiveBinding.Tests/` (NUnit), `ReactiveBinding.Package/` (NuGet packaging), `.sln`, `Directory.Build.props`. Outside the Unity package, so Unity never compiles it.

The runtime C# source has a single shared copy under `Unity/Runtime/`; the NuGet projects reference it via `<Compile Include="..\..\Unity\Runtime\**\*.cs">`.

## NuGet packaging

`NuGet/ReactiveBinding.Package/ReactiveBinding.Package.csproj` packs the runtime (`Unity/Runtime/**/*.cs` → `lib/`) plus the generator (→ `analyzers/dotnet/cs/`) into a single NuGet package `XuToWei.ReactiveBinding`. Published to nuget.org on `v*` tags via `.github/workflows/publish-nuget.yml` (needs repo secret `NUGET_API_KEY`). See `NuGet/ReactiveBinding.Package/README.md` for the full publishing guide. Manual: `dotnet pack NuGet/ReactiveBinding.Package/ReactiveBinding.Package.csproj -c Release`.

## Architecture

### Directory Structure
- `Unity/Runtime/` - Attributes, interfaces (`IReactiveObserver`, `IVersion`), version containers (`VersionList<T>`, `VersionDictionary<K,V>`, `VersionHashSet<T>`); `Unity/Runtime/Sync/` holds `IVersionSyncable` and `[VersionSync]`
- `NuGet/ReactiveBinding.Generator/` - Generator implementation
- `NuGet/ReactiveBinding.Tests/` - NUnit tests

### Core Components

**Generators** (`ISourceGenerator`):
- **ReactiveBindGenerator** - Generates `ObserveChanges()` and `ResetChanges()` from `[ReactiveSource]`/`[ReactiveBind]`
- **VersionFieldGenerator** - Generates properties from `[VersionField]` fields with `IVersion` implementation; also generates `IVersionSyncable` (`Commit`/`GetFull`/`GetDelta`/`Apply`/`ApplyDelta` + recursion primitives `WriteFull`/`WriteDelta`/`ReadFull`/`ReadDelta`/`ResetSync`) for classes with `[VersionSync]` fields

**Syntax Receivers** (`ISyntaxContextReceiver`):
- **ReactiveSyntaxReceiver** - Collects `[ReactiveSource]`/`[ReactiveBind]`/`[ReactiveThrottle]`, builds `ReactiveClassData`
- **VersionFieldSyntaxReceiver** - Collects `[VersionField]` fields, `[VersionFieldProperty]` and `[VersionSync]` markers

**Analyzers** (`DiagnosticAnalyzer`):
- **ReservedMethodAnalyzer** - Prevents manual `ObserveChanges()`/`ResetChanges()` (RB1005/RB1006)
- **ObserveChangesCallAnalyzer** - Warns when `ObserveChanges()` not called in class (RB0003), ignored by `[ReactiveObserveIgnore]` or reactive base class
- **ParentAccessAnalyzer** - Prevents `IVersion.Parent` access outside `IVersion` implementations (VF3001)
- **VersionFieldAccessAnalyzer** - Prevents direct access to `[VersionField]` backing fields (VF3002)
- **VersionFieldInitializerAnalyzer** - Prevents default value initializers on `[VersionField]` fields (VF3003)
- **VersionSyncFieldAnalyzer** - Requires `[VersionSync]` fields to also be `[VersionField]` (VS2002)

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

Generates properties from `[VersionField]` fields (`m_Health` → `Health` property, prefix stripped + first letter capitalized):
- Field: must be `private`, must have `m_` prefix
- Class: must be `partial`, must implement `IVersion`
- Generates: `__version`, `Parent`, `Version`, `IncrementVersion()`
- Nested `IVersion` fields: auto-manages parent chain
- Version propagation: changes bubble up through parent chain via `IncrementVersion()`
- Float/double: epsilon comparison (1e-6f / 1e-9d)
- `Parent` only accessible within `IVersion` implementations (VF3001)
- Backing fields (`m_` prefixed) must use generated properties (VF3002)
- `[VersionFieldProperty(Type)]` or `[VersionFieldProperty(string)]` adds custom attributes to generated properties

### Data Synchronization

`[VersionSync]` on a `[VersionField]` field opts it into sync; a class with any such field gets `IVersionSyncable`.
- Model: snapshot baseline + merged field-level delta. `Commit()` serializes the full tree (BCL `BinaryWriter`) into `__syncBaseline` and calls `ResetSync()`; `GetFull()`/`GetDelta()` return `byte[]`; `Apply`/`ApplyDelta` write backing fields directly (no version/dirty re-trigger).
- Per object: `__syncDirty` (ulong, set in synced setters, cleared on Commit), `__syncWatermark` (= `VersionCounter.Current` at last reset, used to prune nested `Version > watermark`).
- Field kinds: Scalar (primitive/enum/string), SyncObject (nested concrete `[VersionSync]` type, Replace/Patch by tag), Container (`VersionList`/`VersionDictionary`/`VersionHashSet` op-log via generated element/key delegate helpers `__wV_s{n}`/`__rV_s{n}`/`__wK_s{n}`/`__rK_s{n}`).
- Containers keep an optional op-log (lazy, enabled by `ResetSync`); `WriteDelta`/`ReadDelta` take element/value patch delegates so changed `IVersion` elements in `VersionList`/`VersionDictionary` are sent as field-level patches (element's own `WriteDelta`) by index/key; `VersionHashSet` is add/remove only; batch/reorder ops fall back to full resend.

### Diagnostics

**RB0xxx** (warnings): RB0001 unmatched source | RB0003 ObserveChanges() not called
**RB1xxx** (class): RB1001 not partial | RB1002 no IReactiveObserver | RB1003 throttle < 1 | RB1004 throttle without interface | RB1005 manual ObserveChanges | RB1006 manual ResetChanges
**RB2xxx** (source): RB2001 void method | RB2002 no getter | RB2003 has params | RB2004 unsupported type | RB2005 no equality op
**RB3xxx** (bind): RB3001 no ids | RB3002 static | RB3003 not void | RB3004 param count | RB3005 type mismatch | RB3006 duplicate | RB3007 no nameof | RB3008 no sources inferred | RB3009 auto-infer with params | RB3010 not marked source
**VF1xxx** (class): VF1001 not partial | VF1002 no IVersion
**VF2xxx** (field): VF2001 no m_ prefix | VF2002 not private | VF2003 property exists
**VF3xxx** (usage): VF3001 Parent access | VF3002 direct field access | VF3003 field has initializer
**VS1xxx** (class): VS1001 more than 64 [VersionSync] fields
**VS2xxx** (field): VS2001 unsupported sync type | VS2002 [VersionSync] without [VersionField]

### Testing

`GeneratorTestHelper` provides: `RunGenerator()`, `RunVersionFieldGenerator()`, `RunReservedMethodAnalyzer()`, `RunObserveChangesCallAnalyzer()`, `RunParentAccessAnalyzer()`, `RunVersionFieldAccessAnalyzer()`, `RunVersionFieldInitializerAnalyzer()`, `RunVersionSyncFieldAnalyzer()`, and `CompileAndRun()` (compiles source + generated code into an in-memory assembly for execution-based round-trip sync tests)

Assertions: `AssertNoErrors()`, `AssertHasDiagnostic(id)`, `AssertGeneratedContains(text)`
