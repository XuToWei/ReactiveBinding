using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveBinding.Generator;

[Generator(LanguageNames.CSharp)]
public class ReactiveBindGenerator : ISourceGenerator
{
    private const string ReactiveSourceAttributeName = "ReactiveBinding.ReactiveSourceAttribute";
    private const string ReactiveBindAttributeName = "ReactiveBinding.ReactiveBindAttribute";
    private const string ReactiveThrottleAttributeName = "ReactiveBinding.ReactiveThrottleAttribute";
    private const string IReactiveObserverName = "ReactiveBinding.IReactiveObserver";
    private const string IVersionInterfaceName = "ReactiveBinding.IVersion";

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new ReactiveSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not ReactiveSyntaxReceiver receiver)
        {
            return;
        }

        foreach (var classData in receiver.ClassDataList)
        {
            ProcessClass(context, classData);
        }
    }

    private void ProcessClass(GeneratorExecutionContext context, ReactiveClassData classData)
    {
        var classSymbol = classData.ClassSymbol;

        // Process auto-inferred bindings before validation
        ProcessAutoInferredBindings(context, classData);

        // Validate class
        if (!ValidateClass(context, classData))
        {
            return;
        }

        // Match sources and bindings
        var sourceDict = classData.Sources.ToDictionary(s => s.MemberName);

        // Validate bindings
        if (!ValidateBindings(context, classData, sourceDict))
        {
            return;
        }

        // Check for unused sources (warning)
        CheckUnusedSources(context, classData, sourceDict);

        // Generate code
        var code = GenerateCode(classData, sourceDict);

        var fileName = $"ReactiveBindGenerator.{classSymbol.ContainingNamespace}.{classSymbol.Name}.g.cs";
        context.AddSource(fileName, code);
    }

    private void ProcessAutoInferredBindings(GeneratorExecutionContext context, ReactiveClassData classData)
    {
        // Build source name set for the analyzer
        var sourceNames = new HashSet<string>(classData.Sources.Select(s => s.MemberName));

        // Get the syntax tree for semantic model lookup
        var syntaxTree = classData.ClassDeclaration.SyntaxTree;
        var compilation = context.Compilation;
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        foreach (var binding in classData.Bindings)
        {
            if (!binding.IsAutoInferred || binding.MethodSyntax == null)
            {
                continue;
            }

            // RB3009: Auto-inferred bindings must have no parameters
            if (binding.ParameterTypes.Length > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB3009_AutoInferredWithParameters,
                    binding.Location,
                    binding.MethodName));
                continue;
            }

            // Analyze the method body to find referenced sources
            var referencedSources = MethodBodyAnalyzer.FindReferencedSources(
                binding.MethodSyntax,
                semanticModel,
                sourceNames,
                classData.ClassSymbol);

            if (referencedSources.Count > 0)
            {
                // Update the binding with inferred source ids
                binding.ReactiveIds = referencedSources.ToArray();
                // Auto-inferred bindings always use nameof semantically
                binding.UsesNameof = true;
            }
            else
            {
                // No sources found - report diagnostic
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB3008_NoSourcesInferred,
                    binding.Location,
                    binding.MethodName));
            }
        }
    }

    private bool ValidateClass(GeneratorExecutionContext context, ReactiveClassData classData)
    {
        var classSymbol = classData.ClassSymbol;
        var classDeclaration = classData.ClassDeclaration;
        bool isValid = true;

        // RB1001: Class must be partial
        if (!classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB1001_ClassNotPartial,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name));
            isValid = false;
        }

        // RB1002: Class must implement IReactiveObserver
        bool implementsInterface = classSymbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == IReactiveObserverName);

        if (!implementsInterface)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB1002_ClassNotImplementInterface,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name));
            isValid = false;
        }

        // RB1003: ReactiveThrottle must be >= 1
        if (classData.ThrottleCallCount.HasValue && classData.ThrottleCallCount.Value < 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB1003_ThrottleInvalidValue,
                classDeclaration.Identifier.GetLocation(),
                classData.ThrottleCallCount.Value));
            isValid = false;
        }

        // RB1004: ReactiveThrottle without IReactiveObserver
        if (classData.ThrottleCallCount.HasValue && !implementsInterface)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB1004_ThrottleWithoutInterface,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name));
            isValid = false;
        }

        // Validate ReactiveSource members
        foreach (var source in classData.Sources)
        {
            if (!ValidateSource(context, source))
            {
                isValid = false;
            }
        }

        return isValid;
    }

    private bool ValidateSource(GeneratorExecutionContext context, ReactiveSourceData source)
    {
        bool isValid = true;

        // RB2001: Method must have return type (not void)
        if (source.MemberKind == ReactiveSourceKind.Method && source.TypeSymbol.SpecialType == SpecialType.System_Void)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB2001_MethodReturnsVoid,
                source.Location,
                source.MemberName));
            isValid = false;
        }

        // RB2002: Property must have getter
        if (source.MemberKind == ReactiveSourceKind.Property && !source.HasGetter)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB2002_PropertyNoGetter,
                source.Location,
                source.MemberName));
            isValid = false;
        }

        // RB2003: Method must have no parameters
        if (source.MemberKind == ReactiveSourceKind.Method && source.HasParameters)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB2003_MethodHasParameters,
                source.Location,
                source.MemberName));
            isValid = false;
        }

        // RB2004: Type must be primitive, struct, or IVersion
        if (!IsSupportedSourceType(source.TypeSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB2004_UnsupportedSourceType,
                source.Location,
                source.MemberName,
                source.TypeSymbol.ToDisplayString()));
            isValid = false;
        }

        // RB2005: Custom struct must have == operator (skip for IVersion types)
        if (IsCustomStructWithoutEqualityOperator(source.TypeSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB2005_StructMissingEqualityOperator,
                source.Location,
                source.MemberName,
                source.TypeSymbol.ToDisplayString()));
            isValid = false;
        }

        return isValid;
    }

    private bool IsCustomStructWithoutEqualityOperator(ITypeSymbol typeSymbol)
    {
        // Only check custom structs (non-primitive value types that are not enums)
        if (!typeSymbol.IsValueType ||
            typeSymbol.SpecialType != SpecialType.None ||
            typeSymbol.TypeKind == TypeKind.Enum)
        {
            return false;
        }

        // Skip Nullable<T> types (they have built-in == operators)
        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return false;
        }

        // Skip IVersion types (they use version comparison, not ==)
        if (typeSymbol.AllInterfaces.Any(i => i.ToDisplayString() == IVersionInterfaceName))
        {
            return false;
        }

        // Check if the struct has op_Equality (== operator)
        var hasEqualityOperator = typeSymbol.GetMembers("op_Equality")
            .OfType<IMethodSymbol>()
            .Any(m => m.MethodKind == MethodKind.UserDefinedOperator);

        return !hasEqualityOperator;
    }

    private bool IsSupportedSourceType(ITypeSymbol typeSymbol)
    {
        // Allow primitive types (int, string, float, bool, etc.)
        if (typeSymbol.SpecialType != SpecialType.None)
        {
            return true;
        }

        // Allow enum types
        if (typeSymbol.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        // Allow structs (value types)
        if (typeSymbol.IsValueType)
        {
            return true;
        }

        // Allow string (even though it's a class)
        if (typeSymbol.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        // Allow IVersion types
        if (typeSymbol.AllInterfaces.Any(i => i.ToDisplayString() == IVersionInterfaceName))
        {
            return true;
        }

        return false;
    }

    private bool ValidateBindings(GeneratorExecutionContext context, ReactiveClassData classData,
        Dictionary<string, ReactiveSourceData> sourceDict)
    {
        bool isValid = true;

        foreach (var binding in classData.Bindings)
        {
            // RB3001: ReactiveBind must have at least one id
            // Skip if auto-inferred (RB3008 is already reported for failed inference)
            if (binding.ReactiveIds.Length == 0)
            {
                if (!binding.IsAutoInferred)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.RB3001_BindEmptyIds,
                        binding.Location,
                        binding.MethodName));
                }
                isValid = false;
                continue;
            }

            // RB3002: Method cannot be static
            if (binding.IsStatic)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB3002_MethodIsStatic,
                    binding.Location,
                    binding.MethodName));
                isValid = false;
            }

            // RB3003: Method must return void
            if (!binding.ReturnsVoid)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB3003_MethodNotVoid,
                    binding.Location,
                    binding.MethodName));
                isValid = false;
            }

            // RB3006: Check for duplicate ids
            var duplicateIds = binding.ReactiveIds
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateIds.Count > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB3006_DuplicateIds,
                    binding.Location,
                    binding.MethodName,
                    string.Join(", ", duplicateIds)));
                isValid = false;
            }

            // RB3007: Parameters must use nameof()
            if (!binding.UsesNameof)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB3007_NotUsingNameof,
                    binding.Location,
                    binding.MethodName));
                isValid = false;
            }

            // Check if all ids have corresponding sources
            var missingIds = binding.ReactiveIds.Where(id => !sourceDict.ContainsKey(id)).ToList();
            if (missingIds.Count > 0)
            {
                // RB0002: Unmatched ReactiveBind
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB0002_UnmatchedBind,
                    binding.Location,
                    binding.MethodName,
                    string.Join(", ", missingIds)));
                isValid = false;
                continue;
            }

            // Check if sources contain version containers
            bool anyVersion = binding.ReactiveIds.Any(id => sourceDict[id].IsVersionContainer);
            bool allNonVersion = !anyVersion;

            // RB3004: Parameter count validation
            int n = binding.ReactiveIds.Length;
            int paramCount = binding.ParameterTypes.Length;

            // Valid param counts:
            // - 0: always valid
            // - N: valid for all (version containers get container, basic types get newValue)
            // - 2N: only valid when NO version containers (old+new pairs)
            bool validParamCount;
            if (allNonVersion)
            {
                // No version containers: 0, N, or 2N
                validParamCount = paramCount == 0 || paramCount == n || paramCount == 2 * n;
            }
            else
            {
                // Has version containers: 0 or N only (no old/new pairs for version containers)
                validParamCount = paramCount == 0 || paramCount == n;
            }

            if (!validParamCount)
            {
                var signatures = GenerateExpectedSignatures(binding, sourceDict);
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB3004_InvalidParameterCount,
                    binding.Location,
                    binding.MethodName,
                    n,
                    paramCount,
                    signatures));
                isValid = false;
                continue;
            }

            // RB3005: Parameter types must match source types
            if (paramCount > 0)
            {
                var expectedTypes = new List<ITypeSymbol>();
                if (paramCount == n)
                {
                    // N params: version containers get container, basic types get newValue
                    foreach (var id in binding.ReactiveIds)
                    {
                        expectedTypes.Add(sourceDict[id].TypeSymbol);
                    }
                }
                else if (paramCount == 2 * n)
                {
                    // 2N params: oldValue, newValue pairs (only for non-version sources)
                    foreach (var id in binding.ReactiveIds)
                    {
                        var sourceType = sourceDict[id].TypeSymbol;
                        expectedTypes.Add(sourceType);
                        expectedTypes.Add(sourceType);
                    }
                }

                for (int i = 0; i < paramCount && i < expectedTypes.Count; i++)
                {
                    if (!SymbolEqualityComparer.Default.Equals(binding.ParameterTypes[i], expectedTypes[i]))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.RB3005_ParameterTypeMismatch,
                            binding.Location,
                            binding.MethodName,
                            i + 1,
                            binding.ParameterTypes[i].ToDisplayString(),
                            expectedTypes[i].ToDisplayString()));
                        isValid = false;
                    }
                }
            }
        }

        return isValid;
    }

    private void CheckUnusedSources(GeneratorExecutionContext context, ReactiveClassData classData,
        Dictionary<string, ReactiveSourceData> sourceDict)
    {
        var usedIds = new HashSet<string>();
        foreach (var binding in classData.Bindings)
        {
            foreach (var id in binding.ReactiveIds)
            {
                usedIds.Add(id);
            }
        }

        foreach (var source in classData.Sources)
        {
            if (!usedIds.Contains(source.MemberName))
            {
                // RB0001: Unmatched ReactiveSource
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB0001_UnmatchedSource,
                    source.Location,
                    source.MemberName));
            }
        }
    }

    private string GenerateCode(ReactiveClassData classData, Dictionary<string, ReactiveSourceData> sourceDict)
    {
        var sb = new StringBuilder();
        var classSymbol = classData.ClassSymbol;
        var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
        var className = classSymbol.Name;

        // Collect all used source ids from bindings
        var usedSourceIds = classData.Bindings
            .SelectMany(b => b.ReactiveIds)
            .Distinct()
            .Where(id => sourceDict.ContainsKey(id))
            .ToList();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        sb.AppendLine($"    partial class {className}");
        sb.AppendLine("    {");

        // Generate fields
        sb.AppendLine("        private bool __reactive_initialized;");

        if (classData.ThrottleCallCount.HasValue && classData.ThrottleCallCount.Value > 1)
        {
            sb.AppendLine("        private int __reactive_callCount;");
        }

        foreach (var id in usedSourceIds)
        {
            var source = sourceDict[id];
            if (source.IsVersionContainer)
            {
                // For version containers, store version number
                sb.AppendLine($"        private int __reactive_{id}_version = -1;");
            }
            else
            {
                var typeName = source.TypeSymbol.ToDisplayString();
                // Keep same type as source, use default! for initialization if needed
                sb.AppendLine($"        private {typeName} __reactive_{id} = default!;");
            }
        }

        sb.AppendLine();

        // Generate ObserveChanges method
        sb.AppendLine("        public void ObserveChanges()");
        sb.AppendLine("        {");

        // Throttle logic
        if (classData.ThrottleCallCount.HasValue && classData.ThrottleCallCount.Value > 1)
        {
            int throttle = classData.ThrottleCallCount.Value;
            sb.AppendLine($"            if (__reactive_initialized && ++__reactive_callCount < {throttle})");
            sb.AppendLine("            {");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("            __reactive_callCount = 0;");
            sb.AppendLine();
        }

        // First call initialization
        sb.AppendLine("            if (!__reactive_initialized)");
        sb.AppendLine("            {");
        sb.AppendLine("                __reactive_initialized = true;");

        // Initialize cache variables
        foreach (var id in usedSourceIds)
        {
            var source = sourceDict[id];
            var accessor = GetSourceAccessor(source);
            if (source.IsVersionContainer)
            {
                sb.AppendLine($"                __reactive_{id}_version = {accessor}?.Version ?? -1;");
            }
            else
            {
                sb.AppendLine($"                __reactive_{id} = {accessor};");
            }
        }

        // Call all bindings with default old value
        foreach (var binding in classData.Bindings)
        {
            var callArgs = GenerateFirstCallArguments(binding, sourceDict);
            sb.AppendLine($"                {binding.MethodName}({callArgs});");
        }

        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();

        // Change detection logic
        // First, find which sources need change flags (used in multi-source bindings)
        var multiBindings = classData.Bindings
            .Where(b => b.ReactiveIds.Length > 1)
            .ToList();
        var sourcesNeedingFlags = new HashSet<string>(multiBindings
            .SelectMany(b => b.ReactiveIds)
            .Distinct());

        // Declare change flags and old values
        foreach (var id in usedSourceIds)
        {
            var source = sourceDict[id];
            if (sourcesNeedingFlags.Contains(id))
            {
                sb.AppendLine($"            bool __changed_{id} = false;");
            }
            if (!source.IsVersionContainer)
            {
                var typeName = source.TypeSymbol.ToDisplayString();
                sb.AppendLine($"            {typeName} __old_{id} = __reactive_{id};");
            }
        }
        sb.AppendLine();

        // Check each source for changes and call single-source bindings
        foreach (var id in usedSourceIds)
        {
            var source = sourceDict[id];
            var accessor = GetSourceAccessor(source);
            var typeName = source.TypeSymbol.ToDisplayString();

            if (source.IsVersionContainer)
            {
                // Version container: compare versions
                sb.AppendLine($"            var __current_{id}_version = {accessor}?.Version ?? -1;");
                sb.AppendLine($"            if (__current_{id}_version != __reactive_{id}_version)");
                sb.AppendLine("            {");
                if (sourcesNeedingFlags.Contains(id))
                {
                    sb.AppendLine($"                __changed_{id} = true;");
                }
                sb.AppendLine($"                __reactive_{id}_version = __current_{id}_version;");

                // Call single-source bindings immediately
                var singleBindings = classData.Bindings
                    .Where(b => b.ReactiveIds.Length == 1 && b.ReactiveIds[0] == id)
                    .ToList();

                foreach (var binding in singleBindings)
                {
                    var callArgs = GenerateMultiSourceCallArguments(binding, sourceDict);
                    sb.AppendLine($"                {binding.MethodName}({callArgs});");
                }

                sb.AppendLine("            }");
            }
            else
            {
                // Non-version: compare values
                // Store the current value first to avoid calling getter twice (comparison + assignment)
                sb.AppendLine($"            {typeName} __current_{id} = {accessor};");
                sb.AppendLine($"            if ({GenerateInequalityCheck($"__current_{id}", $"__reactive_{id}", source.TypeSymbol)})");

                sb.AppendLine("            {");
                if (sourcesNeedingFlags.Contains(id))
                {
                    sb.AppendLine($"                __changed_{id} = true;");
                }

                sb.AppendLine($"                __reactive_{id} = __current_{id};");

                // Call single-source bindings immediately
                var singleBindings = classData.Bindings
                    .Where(b => b.ReactiveIds.Length == 1 && b.ReactiveIds[0] == id)
                    .ToList();

                foreach (var binding in singleBindings)
                {
                    var callArgs = GenerateCallArguments(binding, sourceDict, id);
                    sb.AppendLine($"                {binding.MethodName}({callArgs});");
                }

                sb.AppendLine("            }");
            }
            sb.AppendLine();
        }

        // Call multi-source bindings (multiBindings already declared above)
        if (multiBindings.Count > 0)
        {
            foreach (var binding in multiBindings)
            {
                var condition = string.Join(" || ", binding.ReactiveIds.Select(id => $"__changed_{id}"));
                sb.AppendLine($"            if ({condition})");
                sb.AppendLine("            {");

                // Check if all sources are version
                bool allVersion = binding.ReactiveIds.All(id => sourceDict[id].IsVersionContainer);
                var callArgs = allVersion
                    ? GenerateMultiSourceCallArguments(binding, sourceDict)
                    : GenerateMultiSourceCallArguments(binding, sourceDict);
                sb.AppendLine($"                {binding.MethodName}({callArgs});");
                sb.AppendLine("            }");
                sb.AppendLine();
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");

        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private string GetSourceAccessor(ReactiveSourceData source)
    {
        return source.MemberKind == ReactiveSourceKind.Method
            ? $"{source.MemberName}()"
            : source.MemberName;
    }

    private string GenerateFirstCallArguments(ReactiveBindData binding, Dictionary<string, ReactiveSourceData> sourceDict)
    {
        int n = binding.ReactiveIds.Length;
        int paramCount = binding.ParameterTypes.Length;

        if (paramCount == 0)
        {
            return "";
        }

        var args = new List<string>();

        if (paramCount == n)
        {
            // N params: version containers get accessor, basic types get accessor
            foreach (var id in binding.ReactiveIds)
            {
                var source = sourceDict[id];
                args.Add(GetSourceAccessor(source));
            }
        }
        else if (paramCount == 2 * n)
        {
            // 2N params: oldValue, newValue pairs (only for non-version sources)
            foreach (var id in binding.ReactiveIds)
            {
                var source = sourceDict[id];
                var typeName = source.TypeSymbol.ToDisplayString();
                args.Add($"default({typeName})!");
                args.Add(GetSourceAccessor(source));
            }
        }

        return string.Join(", ", args);
    }

    private string GenerateCallArguments(ReactiveBindData binding, Dictionary<string, ReactiveSourceData> sourceDict, string changedId)
    {
        int paramCount = binding.ParameterTypes.Length;

        if (paramCount == 0)
        {
            return "";
        }

        var source = sourceDict[changedId];
        var args = new List<string>();

        if (paramCount == 1)
        {
            // Single source, newValue/container only
            if (source.IsVersionContainer)
            {
                args.Add(GetSourceAccessor(source));
            }
            else
            {
                args.Add($"__reactive_{changedId}");
            }
        }
        else if (paramCount == 2)
        {
            // Single source, oldValue and newValue (non-version only)
            args.Add($"__old_{changedId}");
            args.Add($"__reactive_{changedId}");
        }

        return string.Join(", ", args);
    }

    private string GenerateMultiSourceCallArguments(ReactiveBindData binding, Dictionary<string, ReactiveSourceData> sourceDict)
    {
        int n = binding.ReactiveIds.Length;
        int paramCount = binding.ParameterTypes.Length;

        if (paramCount == 0)
        {
            return "";
        }

        var args = new List<string>();

        if (paramCount == n)
        {
            // N params: version containers get accessor, basic types get cached value
            foreach (var id in binding.ReactiveIds)
            {
                var source = sourceDict[id];
                if (source.IsVersionContainer)
                {
                    args.Add(GetSourceAccessor(source));
                }
                else
                {
                    args.Add($"__reactive_{id}");
                }
            }
        }
        else if (paramCount == 2 * n)
        {
            // 2N params: oldValue, newValue pairs (only for non-version sources)
            foreach (var id in binding.ReactiveIds)
            {
                args.Add($"__old_{id}");
                args.Add($"__reactive_{id}");
            }
        }

        return string.Join(", ", args);
    }

    private string GenerateExpectedSignatures(ReactiveBindData binding, Dictionary<string, ReactiveSourceData> sourceDict)
    {
        var signatures = new List<string>();
        int n = binding.ReactiveIds.Length;

        // Check if any source is a version container
        bool anyVersion = binding.ReactiveIds.Any(id =>
            sourceDict.TryGetValue(id, out var s) && s.IsVersionContainer);

        // Signature 1: No parameters (always valid)
        signatures.Add($"void {binding.MethodName}()");

        // Signature 2: N parameters (version containers get container, basic types get newValue)
        var nParams = new List<string>();
        foreach (var id in binding.ReactiveIds)
        {
            if (sourceDict.TryGetValue(id, out var source))
            {
                var typeName = source.TypeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                nParams.Add($"{typeName} {id}");
            }
        }
        if (nParams.Count == n)
        {
            signatures.Add($"void {binding.MethodName}({string.Join(", ", nParams)})");
        }

        // Signature 3: 2N parameters (oldValue, newValue pairs) - only when NO version containers
        if (!anyVersion)
        {
            var fullParams = new List<string>();
            foreach (var id in binding.ReactiveIds)
            {
                if (sourceDict.TryGetValue(id, out var source))
                {
                    var typeName = source.TypeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    fullParams.Add($"{typeName} old{id}");
                    fullParams.Add($"{typeName} new{id}");
                }
            }
            if (fullParams.Count == 2 * n)
            {
                signatures.Add($"void {binding.MethodName}({string.Join(", ", fullParams)})");
            }
        }

        return string.Join(" | ", signatures);
    }

    private string GenerateInequalityCheck(string left, string right, ITypeSymbol typeSymbol)
    {
        // Float: use epsilon comparison
        if (typeSymbol.SpecialType == SpecialType.System_Single)
        {
            return $"System.Math.Abs({left} - {right}) > 1e-6f";
        }
        // Double: use epsilon comparison
        if (typeSymbol.SpecialType == SpecialType.System_Double)
        {
            return $"System.Math.Abs({left} - {right}) > 1e-9d";
        }
        // All other types: use !=
        // If struct doesn't implement == operator, compiler will report error
        return $"{left} != {right}";
    }
}
