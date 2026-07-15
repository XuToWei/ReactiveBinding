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
- **Reactive inheritance** - Derived reactive classes chain via `base.ObserveChanges()` automatically; `VersionField`/`IVersionSync` inheritance is deliberately rejected (VF10003)
- **VersionField** - `[VersionField]` auto-generates properties with version tracking and parent chain propagation
- **Custom property attributes** - `[VersionProperty: Attribute(...)]` relays normal Attribute syntax to generated properties
- **Version containers** - `VersionList<T>`, `VersionDictionary<K,V>`, `VersionHashSet<T>` for efficient collection change detection (version-tracking only); their independent sync counterparts `VersionSyncList<T>` / `VersionSyncDictionary<K,V>` / `VersionSyncHashSet<T>` (separate types that also implement `IVersionSync`) are required for synced `[VersionField]` containers
- **Data synchronization** - declaring a `[VersionField]` class as `: IVersionSync` syncs every `[VersionField]`; a `SyncContext` flat registry (id → node) does full-snapshot sync — `CaptureFull(writer)` writes every registered node (by ascending id) into a caller-supplied `BinaryWriter` (each call a complete, self-contained snapshot / keyframe), and `Apply(reader)` rebuilds the consumer to match while advancing local versions for ReactiveBind without marking outbound sync dirty. After a baseline `CaptureFull`, `ctx.CaptureDelta(writer)` sends **coalesced incremental deltas**: every mutation marks its node dirty (a per-node changed-field bitmask; containers keep a per-frame op log), and CaptureDelta writes one record per dirty node (id written once however many fields changed) as a self-contained `[byte 0]` frame, clearing the dirty state; removed nodes are pruned only by a later full keyframe
- **Full diagnostics** - Compile-time error/warning codes catch mistakes early

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
- **VersionFieldGenerator** - Generates properties from `[VersionField]` fields with `IVersion` implementation; also generates the flat-registry `IVersionSync` (`__SyncId`/`__SyncContext`/`AttachTo`/`__CaptureFull`/`__CaptureDelta`/`__Apply`/`__SyncChildren`/`__MarkDirty`/`__MarkAllDirty`/`__ClearDirty` + a private inline `__Recurse` recursion driver), dirty-marking setters (a mutation sets the node's changed-field bitmask, which `CaptureDelta` later finds via a registry scan over `__IsDirty`; `CaptureFull` writes + clears all, so it doubles as the baseline), per-node `[id][mask chunks][payloads]` records (one variable-width mask per 64-field chunk), and inline id-referenced read/write of reference fields (plus container element delegates/factories) for classes that implement `IVersionSync`

**Syntax Receivers** (`ISyntaxContextReceiver`):
- **ReactiveSyntaxReceiver** - Collects `[ReactiveSource]`/`[ReactiveBind]`/`[ReactiveThrottle]`, builds `ReactiveClassData`
- **VersionFieldSyntaxReceiver** - Collects `[VersionField]` fields and their `[VersionProperty: ...]` target lists

**Analyzers** (`DiagnosticAnalyzer`):
- **ReservedMethodAnalyzer** - Prevents manual `ObserveChanges()`/`ResetChanges()` (RB10005/RB10006)
- **ObserveChangesCallAnalyzer** - Warns when `ObserveChanges()` not called in class (RB10009), ignored by `[ReactiveObserveIgnore]` or reactive base class
- **VersionProtocolAccessAnalyzer** - Prevents user code from accessing reserved `IVersion`/`IVersionSync` `__*` protocol members (VF10012), while allowing generated code and exact runtime interfaces/kernel/container types
- **VersionPropertyTargetSuppressor** - Suppresses CS0658 only for `[VersionProperty: ...]` lists on semantic `[VersionField]` fields; the generator binds and relays those attributes
- **VersionInheritanceAnalyzer** - Rejects inheritance from any `IVersion`/`IVersionSync` implementation, including derived classes with no `[VersionField]` (VF10003)
- **VersionFieldAccessAnalyzer** - Prevents direct access to `[VersionField]` backing fields (VF10010)
- **VersionFieldInitializerAnalyzer** - Prevents default value initializers on `[VersionField]` fields (VF10011)

**Helpers**: `MethodBodyAnalyzer` (auto-inference), `ReactiveDataModels`, `DiagnosticDescriptors`

### Code Generation Flow

1. `ReactiveSyntaxReceiver` collects `[ReactiveSource]` members and `[ReactiveBind]` methods
2. For parameterless `[ReactiveBind]` (auto-inference), `MethodBodyAnalyzer` finds referenced sources
3. Generator validates: partial class, implements `IReactiveObserver`, source/binding constraints
4. Generates partial class with:
   - Cache fields (`__reactive_{name}`), `__reactive_initialized` flag, optional `__reactive_callCount`
   - `ObserveChanges()`: `virtual` for every non-sealed reactive root / plain for sealed roots / `override` (with `base.ObserveChanges()`) for derived reactive classes
   - `ResetChanges()`: same modifier pattern as `ObserveChanges()`
   - Derived classes without `[ReactiveBind]` skip generation (inherit from base)

### Auto-Inference Binding

When `[ReactiveBind]` has no parameters, `MethodBodyAnalyzer.FindReferencedSources()` analyzes the method body:
- Detects direct access (`Health`), this access (`this.Health`), method calls (`GetDamage()`)
- Handles local variable shadowing (shadowed names ignored)
- Must have no parameters (RB10024), must find sources (RB10023)

### VersionField Generator

Generates properties from `[VersionField]` fields (`__Health` → `Health` property, prefix stripped + first letter capitalized):
- Field: must be `private`, must have `__` prefix
- Class: must be `partial`, must implement `IVersion`
- Generates: `__Parent`, writable `__Version` protocol storage, `__IncrementVersion()`; `IVersionSync` supplies `SyncId`/`SyncContext`/`IsDirty` as default interface forwarders
- Nested `IVersion` fields: auto-manages parent chain
- Version propagation: changes bubble up through parent chain via `__IncrementVersion()`
- Float/double: epsilon comparison (1e-6f / 1e-9d)
- All `__*` protocol members, including `__Parent`, are reserved for generated/runtime code (VF10012); runtime ownership guards reject an `IVersion` instance appearing in more than one field/container slot
- Backing fields (`__` prefixed) must use generated properties (VF10010)
- `[VersionProperty: Attribute(...)]` adds compile-time-bound custom attributes to generated properties; relayed attributes must support `AttributeTargets.Property` (VF10013)

### Data Synchronization

Sync is opt-in **at the class level**: declare a `[VersionField]` class as `: IVersionSync` and the generator syncs **every** `[VersionField]` in it (there is no per-field attribute). A class declared `: IVersion` (not Sync) gets version tracking only.
- Model: **flat registry + full snapshot (keyframe), with optional coalesced incremental deltas**. A `SyncContext` holds every syncable node in `Dictionary<int, IVersionSync>` keyed by a stable global `__SyncId`; the **caller owns the output stream** (pass a `BinaryWriter` to capture into, a `BinaryReader` to apply from). `CaptureFull(writer)` scans the registry by ascending id and writes every node's full record, then clears every node's dirty state (so it also establishes the baseline). Every mutation marks its node dirty (always — there is no recording flag).
- **Incremental deltas**: after a baseline `CaptureFull`, a mutation **marks its node dirty** (a scalar/reference setter sets the node's slot bit via `__MarkDirty`; a container structural op sets a flag + appends to its per-frame op log). `ctx.CaptureDelta(writer)` then writes a `[byte 0]` marker and **scans the registry by ascending id**, writing one record per node whose `__IsDirty` is set and clearing it — so a node's id is written once no matter how many of its fields changed, repeated writes to a field collapse to the final value, and repeated container ops collapse to one record. Each `CaptureDelta` writes a self-contained frame into the caller's writer; a not-full `Apply` updates in place and never prunes, so **removed object nodes linger in the consumer registry** — ship a periodic full `CaptureFull` keyframe to drop them. `__Apply` writes backing fields / internal container storage directly, advances local `__Version` for ReactiveBind, and never marks the node dirty.
- API: `SyncContext` is a thin registry **kernel** — state (`__Objects` id→node dict, `__NextId` allocator) plus `CaptureFull(BinaryWriter)` / `CaptureDelta(BinaryWriter)` / `Apply(BinaryReader)` and no per-node behaviour. Seed the same root on both sides with `root.AttachTo(ctx)` (registration only, both deterministically assign the root id 1). All registration / removal / resolve / record (de)serialization is generated **inline** on the nodes: each emits `AttachTo(ctx)`, `__CaptureFull(writer)` (full record), `__CaptureDelta(writer)` (changed record), `__Apply(BinaryReader)`, `__IsDirty`/`__MarkDirty`/`__MarkAllDirty`/`__ClearDirty`, `__Reset()` (an **`IVersion`** member — every version node, sync or not, implements it; for a sync node it's object-pool reuse: detaches the subtree root from its parent/context, zeroes ids/dirty/versions, and rebuilds retained internal parent links while keeping field values/container contents so it can be re-`AttachTo`'d), `__SyncChildren(SyncOp)`, and a private `__Recurse(SyncOp, child)` (register/unregister against the exposed state, one copy per type; the unregister case delegates to `__Reset()`, so a node leaving the graph is fully detached/reset). Both `CaptureFull` and `CaptureDelta` drive the nodes purely by id-scan — there is no subtree-walk recursion.
- Wire format: a `[byte isFull]` marker, then a flat list of node records read to EOF. A node record is `[int id][payload]` — `SyncContext.Apply` reads the `[id]` then dispatches to the node's `__Apply`. An **object node** payload is `[mask chunks][changed-field payloads, ascending slot]`, where fields are numbered `0..N-1` by `f.SyncSlot` and grouped into 64-field chunks. Each chunk's mask is a variable-width bitfield of changed slots (`byte`/`ushort`/`uint`/`ulong` sized to that chunk's field count): `__CaptureFull` uses full masks, `__CaptureDelta` dirty masks. A **container node** payload is `[byte full]` then either `[count][elems]` (full) or `[opCount][ (op,args)… ]` (op log). Node ids are assigned pre-order (parent < descendants), so flushing by ascending id (both full and delta) emits a parent's reference record before the children it must create on the consumer. When the marker is full, `Apply` drops any registered node the snapshot didn't mention.
- Reference fields (SyncObject / Container) serialize as the referenced node's `__SyncId` (0 = null). The consumer creates a node the first time a reference to it is read — inline in the node's `__Apply` (`ctx.__Objects.TryGetValue` else `new T()` registered under the wire id) using the field's **static** type — so no type tags travel on the wire. A reference setter keeps the registry current — `__Recurse(Unregister, old)` drops the old subtree, `__Recurse(Attach, value)` registers the new one (assigning ids; for containers injects element delegates via `__InitSync`; marks each freshly attached node all-dirty so its full record flushes) — and marks its own reference slot dirty (before attaching, so parent flushes before children). `Apply` advances local versions but does not create outbound dirty state.
- Containers come in two **independent** types (no inheritance): a version-only one (`VersionList<T>`/`VersionDictionary<K,V>`/`VersionHashSet<T>`, just `IVersion` + collection + version tracking) and a self-contained sync one (`VersionSyncList<T>`/`VersionSyncDictionary<K,V>`/`VersionSyncHashSet<T>`, implementing `IList/IDictionary/ISet` + `IVersion` + `IVersionSync` with the collection, version tracking, element attach/unregister, and op log all inline). A synced `[VersionField]` container **must** be a `VersionSync*` type (a version-only container resolves to None → VS10001); the version-only base is for non-synced version tracking. The sync subclass is a registry node with its own id: a structural op enlists the container (`__EnsureDirty`) and appends to a per-frame op log — single-element ops (`Add/Insert/Set/RemoveAt/Remove/Clear`, plus Dictionary `Set/Remove` and HashSet `Add/Remove`) log a small op; bulk/reorder ops (`AddRange/InsertRange/RemoveRange/RemoveAll/Reverse/Sort/*With`) fall back to a full record (`__MarkAllDirty`). `__CaptureDelta` writes `[id][0][opCount][ops]` (or `[id][1][count][elems]` when fully dirty); `__CaptureFull` writes `[id][1][count][elems]`; `__Apply` reads the `[full]` byte then rebuilds or replays. Scalar elements are inlined via injected `__wElem_/__rElem_` (or `__wKey_/__rKey_/__wVal_/__rVal_` for Dictionary); object elements/values (`VersionSyncList<T>`, `VersionSyncHashSet<T>`, or `VersionSyncDictionary<K,V>` where the element/value type is `IVersionSync`) are registry nodes referenced by id — the op carries the element/value id and the new element/value's own record (flushed because it was marked all-dirty on attach) syncs its fields. Dictionary object **keys** and custom Dictionary/HashSet comparers are not supported.

### Diagnostics

**RB10001–RB10009** (class/general): RB10001 not partial | RB10002 no IReactiveObserver | RB10003 throttle < 1 | RB10004 throttle without interface | RB10005 manual ObserveChanges | RB10006 manual ResetChanges | RB10007 unmatched source | RB10008 unmatched bind | RB10009 ObserveChanges() not called
**RB10010–RB10015** (source): RB10010 void method | RB10011 no getter | RB10012 has params | RB10013 unsupported type | RB10014 no equality op | RB10015 duplicate source id
**RB10016–RB10026** (bind): RB10016 no ids | RB10017 static | RB10018 not void | RB10019 param count | RB10020 type mismatch | RB10021 duplicate | RB10022 no nameof | RB10023 no sources inferred | RB10024 auto-infer with params | RB10025 not marked source | RB10026 generic/ref/out/in callback
**VF10001–VF10004** (class): VF10001 not partial | VF10002 no IVersion | VF10003 inheritance unsupported | VF10004 generated member conflict
**VF10005–VF10009** (field): VF10005 no __ prefix | VF10006 not private | VF10007 property exists | VF10008 invalid modifiers | VF10009 invalid generated name
**VF10010–VF10013** (usage): VF10010 direct field access | VF10011 field has initializer | VF10012 internal `IVersion`/`IVersionSync` `__*` access | VF10013 invalid `VersionProperty` attribute
**VS10001–VS10004** (field/class): VS10001 unsupported synced field type (a `[VersionField]` in an `IVersionSync` class whose type can't be synchronized) | VS10002 synced object type missing public parameterless constructor | VS10003 synced object type must be concrete | VS10004 `VersionSyncDictionary` key must be scalar

### Testing

`GeneratorTestHelper` provides: `RunGenerator()`, `RunVersionFieldGenerator()`, `RunVersionPropertyTargetSuppressor()`, `RunReservedMethodAnalyzer()`, `RunObserveChangesCallAnalyzer()`, `RunVersionProtocolAccessAnalyzer()`, `RunGeneratedVersionProtocolAccessAnalyzer()`, `RunVersionInheritanceAnalyzer()`, `RunVersionFieldAccessAnalyzer()`, `RunVersionFieldInitializerAnalyzer()`, `RunVersionSyncFieldAnalyzer()`, and `CompileAndRun()` (compiles source + generated code into an in-memory assembly for execution-based round-trip sync tests)

Assertions: `AssertNoErrors()`, `AssertHasDiagnostic(id)`, `AssertGeneratedContains(text)`
