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
- **Data synchronization** - declaring a `[VersionField]` class as `: IVersionSync` syncs every `[VersionField]`; a `SyncContext` flat registry (id → node) does direct-write sync — each mutation writes its record straight into the context stream, drained with a single `Commit()` (first commit = full state, later commits = deltas)
- **34 diagnostics** - Compile-time error/warning codes catch mistakes early

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

`NuGet/ReactiveBinding.Package/ReactiveBinding.Package.csproj` packs the runtime (`Unity/Runtime/**/*.cs` → `lib/`) plus the generator (→ `analyzers/dotnet/cs/`) into a single NuGet package `XuToWei.ReactiveBinding`. Published to nuget.org on `v*` tags via `.github/workflows/publish-nuget.yml` using Trusted Publishing/OIDC (no stored key; needs a nuget.org Trusted Publishing policy + repo variable `NUGET_USER`). See `NuGet/ReactiveBinding.Package/README.md` for the full publishing guide. Manual: `dotnet pack NuGet/ReactiveBinding.Package/ReactiveBinding.Package.csproj -c Release`.

## Architecture

### Directory Structure
- `Unity/Runtime/` - Attributes, interfaces (`IReactiveObserver`, `IVersion`, `IVersionSync`), `SyncContext`, version containers (`VersionList<T>`, `VersionDictionary<K,V>`, `VersionHashSet<T>`)
- `NuGet/ReactiveBinding.Generator/` - Generator implementation
- `NuGet/ReactiveBinding.Tests/` - NUnit tests

### Core Components

**Generators** (`ISourceGenerator`):
- **ReactiveBindGenerator** - Generates `ObserveChanges()` and `ResetChanges()` from `[ReactiveSource]`/`[ReactiveBind]`
- **VersionFieldGenerator** - Generates properties from `[VersionField]` fields with `IVersion` implementation; also generates the flat-registry `IVersionSync` (`__SyncId`/`__SyncContext`/`AttachTo`/`__Commit`/`__Apply`/`__SyncChildren` + a private inline `__Recurse` recursion driver), direct-write setters (a mutation writes its record straight into `SyncContext.__Writer`), and inline id-referenced read/write of reference fields (plus container element delegates/factories) for classes that implement `IVersionSync`

**Syntax Receivers** (`ISyntaxContextReceiver`):
- **ReactiveSyntaxReceiver** - Collects `[ReactiveSource]`/`[ReactiveBind]`/`[ReactiveThrottle]`, builds `ReactiveClassData`
- **VersionFieldSyntaxReceiver** - Collects `[VersionField]` fields and `[VersionFieldProperty]` markers

**Analyzers** (`DiagnosticAnalyzer`):
- **ReservedMethodAnalyzer** - Prevents manual `ObserveChanges()`/`ResetChanges()` (RB1005/RB1006)
- **ObserveChangesCallAnalyzer** - Warns when `ObserveChanges()` not called in class (RB0003), ignored by `[ReactiveObserveIgnore]` or reactive base class
- **ParentAccessAnalyzer** - Prevents `IVersion.__Parent` access outside `IVersion` implementations (VF3001)
- **VersionFieldAccessAnalyzer** - Prevents direct access to `[VersionField]` backing fields (VF3002)
- **VersionFieldInitializerAnalyzer** - Prevents default value initializers on `[VersionField]` fields (VF3003)

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
- Generates: `__Parent`, `__Version` (auto-property, private set), `__IncrementVersion()`
- Nested `IVersion` fields: auto-manages parent chain
- Version propagation: changes bubble up through parent chain via `__IncrementVersion()`
- Float/double: epsilon comparison (1e-6f / 1e-9d)
- `__Parent` only accessible within `IVersion` implementations (VF3001)
- Backing fields (`m_` prefixed) must use generated properties (VF3002)
- `[VersionFieldProperty(Type)]` or `[VersionFieldProperty(string)]` adds custom attributes to generated properties

### Data Synchronization

Sync is opt-in **at the class level**: declare a `[VersionField]` class as `: IVersionSync` and the generator syncs **every** `[VersionField]` in it (there is no per-field attribute). A class declared `: IVersion` (not Sync) gets version tracking only.
- Model: **flat registry + direct-write** (no dirty set, no deferred serialization). A `SyncContext` holds every syncable node in `Dictionary<int, IVersionSync>` keyed by a stable global `__SyncId`, and owns the `BinaryWriter`/stream that mutations write into. A mutation (scalar setter, reference assignment, container op) writes its record straight into the context stream the moment it happens; the same field changed N times in a cycle emits N records (no dedup — the consumer applies in order, last wins).
- API: `SyncContext` is a thin registry **kernel** — it exposes state (`__Objects` id→node dict, `__NextId` allocator, `__Writer` BinaryWriter) plus exactly two operations and has no per-node behaviour. Seed the same root on both sides with `root.AttachTo(ctx)` (registration only, both deterministically assign the root id 1). Producer: the writer owns a single never-reallocated stream (append-only log); `MemoryStream ctx.Commit()` advances a cursor and returns that same stream positioned at the records written since the last call (consume it — read/ship its bytes — before the next mutation) — the first commit (after attaching an empty root) is the full state, each later commit is the delta. Consumer: `ctx.Apply(BinaryReader)` reads from the reader's position to EOF and applies either. All registration / removal / resolve / subtree recursion is generated **inline** on the nodes: each emits `AttachTo(ctx)`, `__Commit()` (writes into `ctx.__Writer`), `__Apply(BinaryReader)` (resolves referenced nodes inline via `ctx.__Objects`), `__SyncChildren(SyncOp)`, and a private `__Recurse(SyncOp, child)` that does register/unregister/write-subtree against the exposed state (one copy per type).
- Wire format: a flat list of self-describing records read until EOF. `[0][int id][payload]` is a node record — the node's `__Apply` reads one unit (object: `[byte slot][payload]`; container: `[byte mode]`, mode 1 = full `[count][elems]`, mode 0 = one op). `[1][int id]` is a removal. Each synced `[VersionField]` is numbered by its index among the class's fields (`f.SyncSlot`, ≤64). Node ids are assigned pre-order (parent < descendants), so a parent's reference record is written before the referenced node's own records (a reference setter writes the parent record, then `WriteSubtree`s the new subtree).
- Reference fields (SyncObject / Container) serialize as the referenced node's `__SyncId` (0 = null). The consumer creates a node the first time a reference to it is read — inline in the node's `__Apply` (`ctx.__Objects.TryGetValue` else `new T()` registered under the wire id) using the field's **static** type — so no type tags travel on the wire. Object-like assignment in a setter unregisters the old subtree (`__Recurse(Unregister, old)` writes a removal record per node), then `__Recurse(Attach, value)`-registers the new one (for containers also injects element delegates via `__InitSync`), writes the parent's reference record, and emits the new subtree via `__Recurse(WriteSubtree, value)`. `Apply` mutates silently (never writes back).
- Containers implement `IVersionSync` (registry nodes with their own id). Each structural op writes its record immediately (List: insert/removeAt/set/clear; HashSet: add/remove/clear; Dictionary: set/remove/clear); batch ops (AddRange/Sort/RemoveAll/set-algebra/…) resend the whole container as a full subtree via `__Recurse(WriteSubtree, this)`. Scalar elements are inlined via injected `__wElem_/__rElem_` (or `__wKey_/__rKey_/__wVal_/__rVal_` for Dictionary); object elements (`VersionList<T>` where `T` is an `IVersionSync` type) are registry nodes referenced by id — an insert/set op carries the element id followed by the element's own subtree, and the element syncs its fields independently (the owner injects an element factory `__new_<Prop>`). Dictionary object **values** and HashSet object **elements** are not supported (VS2001).

### Diagnostics

**RB0xxx** (warnings): RB0001 unmatched source | RB0003 ObserveChanges() not called
**RB1xxx** (class): RB1001 not partial | RB1002 no IReactiveObserver | RB1003 throttle < 1 | RB1004 throttle without interface | RB1005 manual ObserveChanges | RB1006 manual ResetChanges
**RB2xxx** (source): RB2001 void method | RB2002 no getter | RB2003 has params | RB2004 unsupported type | RB2005 no equality op
**RB3xxx** (bind): RB3001 no ids | RB3002 static | RB3003 not void | RB3004 param count | RB3005 type mismatch | RB3006 duplicate | RB3007 no nameof | RB3008 no sources inferred | RB3009 auto-infer with params | RB3010 not marked source
**VF1xxx** (class): VF1001 not partial | VF1002 no IVersion
**VF2xxx** (field): VF2001 no m_ prefix | VF2002 not private | VF2003 property exists
**VF3xxx** (usage): VF3001 __Parent access | VF3002 direct field access | VF3003 field has initializer
**VS2xxx** (field): VS2001 unsupported synced field type (a `[VersionField]` in an `IVersionSync` class whose type can't be synchronized)

### Testing

`GeneratorTestHelper` provides: `RunGenerator()`, `RunVersionFieldGenerator()`, `RunReservedMethodAnalyzer()`, `RunObserveChangesCallAnalyzer()`, `RunParentAccessAnalyzer()`, `RunVersionFieldAccessAnalyzer()`, `RunVersionFieldInitializerAnalyzer()`, `RunVersionSyncFieldAnalyzer()`, and `CompileAndRun()` (compiles source + generated code into an in-memory assembly for execution-based round-trip sync tests)

Assertions: `AssertNoErrors()`, `AssertHasDiagnostic(id)`, `AssertGeneratedContains(text)`
