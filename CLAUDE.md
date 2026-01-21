# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ReactiveBinding is a C# Source Generator that provides compile-time reactive data binding for Unity. It generates change detection code at compile time, eliminating runtime reflection overhead.

## Build Commands

```bash
# Build the source generator (from Generator~ directory)
dotnet build Generator~/Core/ReactiveBinding.Generator.csproj

# Run tests
dotnet test Generator~/Tests/ReactiveBinding.Generator.Tests.csproj

# Run a specific test
dotnet test Generator~/Tests/ReactiveBinding.Generator.Tests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

Note: The generator DLL is automatically copied to `Runtime/Plugins/` after build via a PostBuild target.

## Architecture

### Directory Structure
- `Runtime/` - Unity runtime code: attributes (`ReactiveSourceAttribute`, `ReactiveBindAttribute`, `ReactiveThrottleAttribute`) and `IReactiveObserver` interface
- `Generator~/` - Source generator (tilde suffix excludes from Unity compilation)
  - `Core/` - Generator implementation
  - `Tests/` - NUnit tests for the generator

### Source Generator Components

The generator implements `ISourceGenerator` and uses `ISyntaxContextReceiver` for syntax collection:

1. **ReactiveSyntaxReceiver** (`ReactiveSyntaxReceiver.cs`) - Collects classes with reactive attributes during syntax analysis, building `ReactiveClassData` objects containing sources and bindings
2. **ReactiveBindGenerator** (`ReactiveBindGenerator.cs`) - Main generator that validates collected data and generates the `ObserveChanges()` implementation
3. **MethodBodyAnalyzer** (`MethodBodyAnalyzer.cs`) - Analyzes method bodies to find referenced `[ReactiveSource]` members for auto-inference binding
4. **ReactiveDataModels** (`ReactiveDataModels.cs`) - Data structures: `ReactiveClassData`, `ReactiveSourceData`, `ReactiveBindData`
5. **DiagnosticDescriptors** (`DiagnosticDescriptors.cs`) - 20 diagnostic codes (RB0xxx warnings, RB1xxx class errors, RB2xxx source errors, RB3xxx binding errors)

### Code Generation Flow

1. `ReactiveSyntaxReceiver` collects members with `[ReactiveSource]` and methods with `[ReactiveBind]`
2. For `[ReactiveBind]` without parameters (auto-inference), `MethodBodyAnalyzer` finds referenced sources in method body
3. Generator validates: partial class, implements `IReactiveObserver`, source/binding constraints
4. Generates partial class with:
   - Cache fields (`__reactive_{name}`) for each used source
   - `__reactive_initialized` flag for first-call detection
   - Optional `__reactive_callCount` for throttling
   - `ObserveChanges()` method with change detection logic

### Auto-Inference Binding

When `[ReactiveBind]` is used without parameters:
- `MethodBodyAnalyzer.FindReferencedSources()` analyzes the method body
- Detects direct access (`Health`), this access (`this.Health`), and method calls (`GetDamage()`)
- Handles local variable shadowing (shadowed names are ignored)
- Auto-inferred methods must have no parameters (RB3009 error otherwise)
- If no sources found, reports RB3008 error

### Testing Pattern

Tests use `GeneratorTestHelper.RunGenerator(source)` which:
- Wraps source with standard usings
- Creates compilation with runtime references
- Returns `GeneratorRunResult` with generated sources and diagnostics

Assertion helpers: `AssertNoErrors()`, `AssertHasDiagnostic(id)`, `AssertGeneratedContains(text)`
