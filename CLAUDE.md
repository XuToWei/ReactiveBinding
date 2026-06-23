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
- **Version containers** - `VersionList<T>`, `VersionDictionary<K,V>`, `VersionHashSet<T>` for efficient collection change detection (version-tracking only); their independent sync counterparts `VersionSyncList<T>` / `VersionSyncDictionary<K,V>` / `VersionSyncHashSet<T>` (separate types that also implement `IVersionSync`) are required for synced `[VersionField]` containers
- **Data synchronization** - declaring a `[VersionField]` class as `: IVersionSync` syncs every `[VersionField]`; a `SyncContext` flat registry (id → node) does full-snapshot sync — `CaptureFull(writer)` writes every registered node (by ascending id) into a caller-supplied `BinaryWriter` (each call a complete, self-contained snapshot / keyframe), and `Apply(reader)` rebuilds the consumer to match. After a baseline `CaptureFull`, `ctx.CaptureDelta(writer)` sends **coalesced incremental deltas**: every mutation marks its node dirty (a per-node changed-field bitmask; containers keep a per-frame op log), and CaptureDelta writes one record per dirty node (id written once however many fields changed) as a self-contained `[byte 0]` frame, clearing the dirty state; removed nodes are pruned only by a later full keyframe
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

`NuGet/ReactiveBinding.Package/ReactiveBinding.Package.csproj` packs the runtime (`Unity/Runtime/**/*.cs` → `lib/`) plus the generator (→ `analyzers/dotnet/cs/`) into a single NuGet package `XuToWei.ReactiveBinding`. Published to nuget.org on `v*` tags via `.github/workflows/publish-nuget.yml` using Trusted Publishing/OIDC (no stored key; needs a nuget.org Trusted Publishing policy + repo variable `NUGET_USER`). See `NuGet/ReactiveBinding.Package/README.md` for the full publishing guide. Manual: `dotnet pack NuGet/ReactiveBinding.Package/ReactiveBinding.Package.csproj -c Release`.

## Architecture

### Directory Structure
- `Unity/Runtime/` - Attributes, interfaces (`IReactiveObserver`, `IVersion`, `IVersionSync`), `SyncContext`, version containers (`VersionList<T>`/`VersionDictionary<K,V>`/`VersionHashSet<T>`, version-only) + their independent `VersionSync*` sync counterparts
- `NuGet/ReactiveBinding.Generator/` - Generator implementation
- `NuGet/ReactiveBinding.Tests/` - NUnit tests

### Core Components

**Generators** (`ISourceGenerator`):
- **ReactiveBindGenerator** - Generates `ObserveChanges()` and `ResetChanges()` from `[ReactiveSource]`/`[ReactiveBind]`
- **VersionFieldGenerator** - Generates properties from `[VersionField]` fields with `IVersion` implementation; also generates the flat-registry `IVersionSync` (`__SyncId`/`__SyncContext`/`AttachTo`/`__CaptureFull`/`__CaptureDelta`/`__Apply`/`__SyncChildren`/`__MarkDirty`/`__MarkAllDirty`/`__ClearDirty` + a private inline `__Recurse` recursion driver), dirty-marking setters (a mutation sets the node's changed-field bitmask, which `CaptureDelta` later finds via a registry scan over `__IsDirty`; `CaptureFull` writes + clears all, so it doubles as the baseline), per-node `[id][mask][payloads]` records (variable-width mask, ≤64 fields), and inline id-referenced read/write of reference fields (plus container element delegates/factories) for classes that implement `IVersionSync`

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

Generates properties from `[VersionField]` fields (`__Health` → `Health` property, prefix stripped + first letter capitalized):
- Field: must be `private`, must have `__` prefix
- Class: must be `partial`, must implement `IVersion`
- Generates: `__Parent`, `__Version` (auto-property, private set), `__IncrementVersion()`
- Nested `IVersion` fields: auto-manages parent chain
- Version propagation: changes bubble up through parent chain via `__IncrementVersion()`
- Float/double: epsilon comparison (1e-6f / 1e-9d)
- `__Parent` only accessible within `IVersion` implementations (VF3001)
- Backing fields (`__` prefixed) must use generated properties (VF3002)
- `[VersionFieldProperty(Type)]` or `[VersionFieldProperty(string)]` adds custom attributes to generated properties

### Data Synchronization

Sync is opt-in **at the class level**: declare a `[VersionField]` class as `: IVersionSync` and the generator syncs **every** `[VersionField]` in it (there is no per-field attribute). A class declared `: IVersion` (not Sync) gets version tracking only.
- Model: **flat registry + full snapshot (keyframe), with optional coalesced incremental deltas**. A `SyncContext` holds every syncable node in `Dictionary<int, IVersionSync>` keyed by a stable global `__SyncId`; the **caller owns the output stream** (pass a `BinaryWriter` to capture into, a `BinaryReader` to apply from). `CaptureFull(writer)` scans the registry by ascending id and writes every node's full record, then clears every node's dirty state (so it also establishes the baseline). Every mutation marks its node dirty (always — there is no recording flag).
- **Incremental deltas**: after a baseline `CaptureFull`, a mutation **marks its node dirty** (a scalar/reference setter sets the node's slot bit via `__MarkDirty`; a container structural op sets a flag + appends to its per-frame op log). `ctx.CaptureDelta(writer)` then writes a `[byte 0]` marker and **scans the registry by ascending id**, writing one record per node whose `__IsDirty` is set and clearing it — so a node's id is written once no matter how many of its fields changed, repeated writes to a field collapse to the final value, and repeated container ops collapse to one record. Each `CaptureDelta` writes a self-contained frame into the caller's writer; a not-full `Apply` updates in place and never prunes, so **removed object nodes linger in the consumer registry** — ship a periodic full `CaptureFull` keyframe to drop them. `__Apply` writes backing fields / internal container storage directly (not via setters/public mutators), so it never re-triggers recording.
- API: `SyncContext` is a thin registry **kernel** — state (`__Objects` id→node dict, `__NextId` allocator) plus `CaptureFull(BinaryWriter)` / `CaptureDelta(BinaryWriter)` / `Apply(BinaryReader)` and no per-node behaviour. Seed the same root on both sides with `root.AttachTo(ctx)` (registration only, both deterministically assign the root id 1). All registration / removal / resolve / record (de)serialization is generated **inline** on the nodes: each emits `AttachTo(ctx)`, `__CaptureFull(writer)` (full record), `__CaptureDelta(writer)` (changed record), `__Apply(BinaryReader)`, `__IsDirty`/`__MarkDirty`/`__MarkAllDirty`/`__ClearDirty`, `__Reset()` (an **`IVersion`** member — every version node, sync or not, implements it; for a sync node it's object-pool reuse: detaches the whole subtree from its context, zeroes ids/dirty/version/`__Parent`, keeps field values/container contents so it can be re-`AttachTo`'d), `__SyncChildren(SyncOp)`, and a private `__Recurse(SyncOp, child)` (register/unregister against the exposed state, one copy per type; the unregister case delegates to `__Reset()`, so a node leaving the graph is fully detached/reset). Both `CaptureFull` and `CaptureDelta` drive the nodes purely by id-scan — there is no subtree-walk recursion.
- Wire format: a `[byte isFull]` marker, then a flat list of node records read to EOF. A node record is `[int id][payload]` — `SyncContext.Apply` reads the `[id]` then dispatches to the node's `__Apply`. An **object node** payload is `[mask][changed-field payloads, ascending slot]`, where the mask is a variable-width bitfield of changed slots (`byte`/`ushort`/`uint`/`ulong` sized to the field count): `__CaptureFull` uses the full mask, `__CaptureDelta` the dirty mask. Each synced `[VersionField]` is numbered `0..N-1` by `f.SyncSlot`; a class may sync at most **64** fields (the mask is 64-bit — VS2002 otherwise). A **container node** payload is `[byte full]` then either `[count][elems]` (full) or `[opCount][ (op,args)… ]` (op log). Node ids are assigned pre-order (parent < descendants), so flushing by ascending id (both full and delta) emits a parent's reference record before the children it must create on the consumer. When the marker is full, `Apply` drops any registered node the snapshot didn't mention.
- Reference fields (SyncObject / Container) serialize as the referenced node's `__SyncId` (0 = null). The consumer creates a node the first time a reference to it is read — inline in the node's `__Apply` (`ctx.__Objects.TryGetValue` else `new T()` registered under the wire id) using the field's **static** type — so no type tags travel on the wire. A reference setter keeps the registry current — `__Recurse(Unregister, old)` drops the old subtree, `__Recurse(Attach, value)` registers the new one (assigning ids; for containers injects element delegates via `__InitSync`; while recording, marks each freshly attached node all-dirty so its full record flushes) — and, while recording, marks its own reference slot dirty (before attaching, so parent flushes before children). `Apply` mutates silently.
- Containers come in two **independent** types (no inheritance): a version-only one (`VersionList<T>`/`VersionDictionary<K,V>`/`VersionHashSet<T>`, just `IVersion` + collection + version tracking) and a self-contained sync one (`VersionSyncList<T>`/`VersionSyncDictionary<K,V>`/`VersionSyncHashSet<T>`, implementing `IList/IDictionary/ISet` + `IVersion` + `IVersionSync` with the collection, version tracking, element attach/unregister, and op log all inline). A synced `[VersionField]` container **must** be a `VersionSync*` type (a version-only container resolves to None → VS2001). A synced `[VersionField]` container **must** be a `VersionSync*` variant (a base container resolves to None → VS2001); the version-only base is for non-synced version tracking. The sync subclass is a registry node with its own id: a structural op enlists the container (`__EnsureDirty`) and appends to a per-frame op log — single-element ops (`Add/Insert/Set/RemoveAt/Remove/Clear`, plus Dictionary `Set/Remove` and HashSet `Add/Remove`) log a small op; bulk/reorder ops (`AddRange/InsertRange/RemoveRange/RemoveAll/Reverse/Sort/*With`) fall back to a full record (`__MarkAllDirty`). `__CaptureDelta` writes `[id][0][opCount][ops]` (or `[id][1][count][elems]` when fully dirty); `__CaptureFull` writes `[id][1][count][elems]`; `__Apply` reads the `[full]` byte then rebuilds or replays. Scalar elements are inlined via injected `__wElem_/__rElem_` (or `__wKey_/__rKey_/__wVal_/__rVal_` for Dictionary); object elements (`VersionSyncList<T>` where `T` is `IVersionSync`) are registry nodes referenced by id — the op carries the element id and the new element's own record (flushed because it was marked all-dirty on attach) syncs its fields. Dictionary object **values** and HashSet object **elements** are not supported (VS2001).

### Diagnostics

**RB0xxx** (warnings): RB0001 unmatched source | RB0003 ObserveChanges() not called
**RB1xxx** (class): RB1001 not partial | RB1002 no IReactiveObserver | RB1003 throttle < 1 | RB1004 throttle without interface | RB1005 manual ObserveChanges | RB1006 manual ResetChanges
**RB2xxx** (source): RB2001 void method | RB2002 no getter | RB2003 has params | RB2004 unsupported type | RB2005 no equality op
**RB3xxx** (bind): RB3001 no ids | RB3002 static | RB3003 not void | RB3004 param count | RB3005 type mismatch | RB3006 duplicate | RB3007 no nameof | RB3008 no sources inferred | RB3009 auto-infer with params | RB3010 not marked source
**VF1xxx** (class): VF1001 not partial | VF1002 no IVersion
**VF2xxx** (field): VF2001 no __ prefix | VF2002 not private | VF2003 property exists
**VF3xxx** (usage): VF3001 __Parent access | VF3002 direct field access | VF3003 field has initializer
**VS2xxx** (field/class): VS2001 unsupported synced field type (a `[VersionField]` in an `IVersionSync` class whose type can't be synchronized) | VS2002 too many synced fields (an `IVersionSync` class with > 64 `[VersionField]`s; the per-node change mask is 64-bit)

### Testing

`GeneratorTestHelper` provides: `RunGenerator()`, `RunVersionFieldGenerator()`, `RunReservedMethodAnalyzer()`, `RunObserveChangesCallAnalyzer()`, `RunParentAccessAnalyzer()`, `RunVersionFieldAccessAnalyzer()`, `RunVersionFieldInitializerAnalyzer()`, `RunVersionSyncFieldAnalyzer()`, and `CompileAndRun()` (compiles source + generated code into an in-memory assembly for execution-based round-trip sync tests)

Assertions: `AssertNoErrors()`, `AssertHasDiagnostic(id)`, `AssertGeneratedContains(text)`
