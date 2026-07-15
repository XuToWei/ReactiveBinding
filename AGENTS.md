# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

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
- **Data synchronization** - declaring a `[VersionField]` class as `: IVersionSync` syncs every `[VersionField]`; a `SyncContext` flat registry (id → node) does full-snapshot sync — `CaptureFull(writer)` writes every registered node (by ascending id) into a caller-supplied `BinaryWriter` (each call a complete, self-contained snapshot / keyframe), and `Apply(reader)` rebuilds the consumer to match without marking outbound sync dirty. After a baseline `CaptureFull`, `ctx.CaptureDelta(writer)` sends **coalesced incremental deltas**: a clean→dirty transition enlists the node id, CaptureDelta sorts and writes only those ids, and its tombstone trailer immediately resets consumer subtrees removed by the producer. Each applied frame assigns one shared new version to every touched sync node/ancestor. Frames are self-delimiting and use varuint for ids/counts/indexes/op counts. A context pair is single-writer: bidirectional peers need separate writer/id namespaces
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
- **VersionFieldGenerator** - Generates properties from `[VersionField]` fields with `IVersion` implementation, including the public `Version`/`Reset` façade over its `__*` runtime protocol; `IVersionSync` supplies the read-only `SyncId`/`SyncContext`/`IsDirty` façade as default interface members, while sync classes generate only the flat-registry storage/behavior (`__SyncId`/`__SyncContext`/`AttachTo`/`__CaptureFull`/`__CaptureDelta`/`__Apply`/`__SyncChildren`/dirty members + a private inline `__Recurse` recursion driver), dirty-marking setters, per-node records, inline reference codecs/factories, and tombstone enlistment

**Syntax Receivers** (`ISyntaxContextReceiver`):
- **ReactiveSyntaxReceiver** - Collects `[ReactiveSource]`/`[ReactiveBind]`/`[ReactiveThrottle]`, builds `ReactiveClassData`
- **VersionFieldSyntaxReceiver** - Collects `[VersionField]` fields and their `[VersionProperty: ...]` target lists

**Analyzers** (`DiagnosticAnalyzer`):
- **ReservedMethodAnalyzer** - Prevents manual `ObserveChanges()`/`ResetChanges()` (RB10005/RB10006)
- **ObserveChangesCallAnalyzer** - Warns when `ObserveChanges()` not called in class (RB10009), ignored by `[ReactiveObserveIgnore]` or reactive base class
- **VersionProtocolAccessAnalyzer** - Prevents user code from reading, writing, invoking, or capturing any `__*` member declared on an `IVersion`/`IVersionSync` implementation (VF10012). Generated code and the exact runtime interfaces/kernel/container types are allowed.
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
- Generates the public `Version`/`Reset` API plus internal `__Parent`, `__Version` (public protocol setter, statically blocked from business code), `__IncrementVersion()`, and `__Reset()` protocol members
- Nested `IVersion` fields: auto-manages parent chain
- Version propagation: changes bubble up through parent chain via `__IncrementVersion()`
- Float/double: epsilon comparison (1e-6f / 1e-9d)
- All `__*` members are reserved for generated/runtime code (VF10012); business code reads `Version`, calls `Reset`, and uses the read-only sync façade. Runtime ownership guards reject an `IVersion` instance appearing in more than one field/container slot
- Backing fields (`__` prefixed) must use generated properties (VF10010)
- `[VersionProperty: Attribute(...)]` adds compile-time-bound custom attributes to generated properties; relayed attributes must support `AttributeTargets.Property` (VF10013)

### Data Synchronization

Sync is opt-in **at the class level**: declare a `[VersionField]` class as `: IVersionSync` and the generator syncs **every** `[VersionField]` in it (there is no per-field attribute). A class declared `: IVersion` (not Sync) gets version tracking only.
- Model: **flat registry + full snapshot (keyframe), with optional coalesced incremental deltas**. A `SyncContext` holds every syncable node in `Dictionary<int, IVersionSync>` keyed by a stable global `__SyncId`; the **caller owns the output stream** (pass a `BinaryWriter` to capture into, a `BinaryReader` to apply from). `CaptureFull(writer)` writes the active registry in ascending-id order, choosing a dense id-range scan or a sorted active-id scan according to sparsity, then clears dirty ids and pending tombstones (so it also establishes the baseline). Every mutation marks its node dirty (always — there is no recording flag).
- **Incremental deltas**: after a baseline `CaptureFull`, a mutation marks its node dirty (a scalar/reference setter sets the node's slot bit; a container structural op sets a flag + appends to its per-frame op log). On the clean→dirty transition, the node calls `ctx.__EnlistDirty(id)`; `CaptureDelta` sorts and visits only these ids, reducing capture work from registry-size `N` to dirty count `K`. A removed subtree calls `ctx.__RecordTombstone(oldId)` before reset. Delta writes normal records first and sorted tombstones second, so the consumer applies the parent reference/container op before immediately resetting the removed subtree. Repeated writes to one scalar field collapse to its final value; container op logs remain ordered and can fall back to a full record.
- API: `SyncContext` is a registry **kernel** — state (`__Objects` id→node dict, `__NextId` allocator), reusable dirty/tombstone/version scratch, `CaptureFull(BinaryWriter)` / `CaptureDelta(BinaryWriter)` / `Apply(BinaryReader)`, plus low-frequency `Compact()` / `TrimScratch()`. Seed the same root on both sides with `root.AttachTo(ctx)` (registration only, both deterministically assign the root id 1). A producer/consumer pair has one authoritative id allocator; applying mutations captured independently by both peers into the same namespace is unsupported. Registration / resolve / record serialization remains generated inline on nodes: each emits `AttachTo`, capture/apply methods, dirty methods, `__Reset`, `__SyncChildren`, and private `__Recurse`; writable `__Version` is inherited once from `IVersion`. Unregister records a tombstone then resets the subtree. Capture is id-driven, not a subtree write walk.
- Wire format: `[byte isFull][positive varuint node id + payload ...][varuint 0][varuint tombstoneCount][varuint tombstone ids ...]`. The zero id terminates normal records, making each frame self-delimiting and allowing consecutive frames in one stream. An **object node** payload is `[mask chunks][changed-field payloads, ascending slot]`; mask chunks remain `byte`/`ushort`/`uint`/`ulong` according to field count. A **container node** payload is `[byte full]` then either `[varuint count][elems]` or `[varuint opCount][(op,args)…]`; non-negative indexes also use varuint. Node/reference ids, counts, indexes, op counts, and tombstone ids are non-negative `int` values encoded with `WriteVarInt32`; scalar field payloads keep their fixed type-specific encoding. `SyncContext.__AllocateId()` allocates only `1..int.MaxValue - 1`, preserving zero as the sentinel and preventing negative ids after overflow. Parent ids precede descendants.
- Reference fields (SyncObject / Container) serialize as a varuint `__SyncId` (0 = null). The consumer creates a node the first time a reference is read inline in `__Apply`, using the field's **static** type; no type tags travel on the wire. A reference setter records tombstones for the old subtree, attaches the replacement, and marks its own slot before the child flush. Apply writes backing fields/internal container storage directly and calls `__TouchVersion`; after the complete frame, the context obtains one new version and assigns the inherited `IVersion.__Version` once on every touched sync node and sync ancestor. This preserves ReactiveBind change detection without repeated parent propagation or outbound dirty state.
- Containers come in two **independent** types (no inheritance): a version-only one (`VersionList<T>`/`VersionDictionary<K,V>`/`VersionHashSet<T>`, just `IVersion` + collection + version tracking) and a self-contained sync one (`VersionSyncList<T>`/`VersionSyncDictionary<K,V>`/`VersionSyncHashSet<T>`, implementing `IList/IDictionary/ISet` + `IVersion` + `IVersionSync` with collection state, version tracking, element attach/unregister, and op logging inline). A synced `[VersionField]` container **must** be a `VersionSync*` type (a version-only container resolves to None → VS10001). List range changes use range opcodes and adjacent Set operations at one stable index collapse to the final value; Dictionary operations use last-write-wins per key; HashSet scalar inverse operations cancel and scalar/object bulk changes emit add/remove differences. Reverse/Sort and logs whose estimated encoded size is no smaller than the final contents use a full record. Both list types expose `SortIfNeeded` to avoid version/dirty changes for already ordered data. `__CaptureDelta` writes `[varuint id][0][varuint opCount][ops]` (or `[varuint id][1][varuint count][elems]` when fully dirty); `__CaptureFull` writes `[varuint id][1][varuint count][elems]`; list indexes and object element/value ids are also varuint. `__Apply` reads the full byte then rebuilds or replays. Scalar elements are inlined through per-type owner codecs shared by matching fields; object elements/values are registry nodes referenced by id and sync their own fields independently. A standalone sync container must be configured through public `InitSync` overloads before `AttachTo`; generated fields inject the internal delegates automatically. Both HashSet types use `EqualityComparer<T>.Default`; elements must keep equality/hash fields stable while stored, and sync-object elements should normally retain reference equality because Apply inserts references before child field records. Dictionary object **keys** and custom Dictionary/HashSet comparers are not supported.

### Diagnostics

**RB10001–RB10009** (class/general): RB10001 not partial | RB10002 no IReactiveObserver | RB10003 throttle < 1 | RB10004 throttle without interface | RB10005 manual ObserveChanges | RB10006 manual ResetChanges | RB10007 unmatched source | RB10008 unmatched bind | RB10009 ObserveChanges() not called
**RB10010–RB10015** (source): RB10010 void method | RB10011 no getter | RB10012 has params | RB10013 unsupported type | RB10014 no equality op | RB10015 duplicate source id
**RB10016–RB10026** (bind): RB10016 no ids | RB10017 static | RB10018 not void | RB10019 param count | RB10020 type mismatch | RB10021 duplicate | RB10022 no nameof | RB10023 no sources inferred | RB10024 auto-infer with params | RB10025 not marked source | RB10026 generic/ref/out/in callback
**VF10001–VF10004** (class): VF10001 not partial | VF10002 no IVersion | VF10003 inheritance unsupported | VF10004 generated member conflict
**VF10005–VF10009** (field): VF10005 no __ prefix | VF10006 not private | VF10007 property exists | VF10008 invalid modifiers | VF10009 invalid generated name
**VF10010–VF10013** (usage): VF10010 direct field access | VF10011 field has initializer | VF10012 internal `IVersion`/`IVersionSync` `__*` access | VF10013 invalid `VersionProperty` attribute
**VS10001–VS10004** (field/class): VS10001 unsupported synced field type | VS10002 synced object type missing public parameterless constructor | VS10003 synced object type must be concrete | VS10004 `VersionSyncDictionary` key must be scalar

### Testing

`GeneratorTestHelper` provides: `RunGenerator()`, `RunVersionFieldGenerator()`, `RunVersionPropertyTargetSuppressor()`, `RunReservedMethodAnalyzer()`, `RunObserveChangesCallAnalyzer()`, `RunVersionProtocolAccessAnalyzer()`, `RunGeneratedVersionProtocolAccessAnalyzer()`, `RunVersionInheritanceAnalyzer()`, `RunVersionFieldAccessAnalyzer()`, `RunVersionFieldInitializerAnalyzer()`, `RunVersionSyncFieldAnalyzer()`, and `CompileAndRun()` (compiles source + generated code into an in-memory assembly for execution-based round-trip sync tests)

Assertions: `AssertNoErrors()`, `AssertHasDiagnostic(id)`, `AssertGeneratedContains(text)`
