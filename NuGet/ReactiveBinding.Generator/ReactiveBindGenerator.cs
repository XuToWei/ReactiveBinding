using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveBinding.Generator;

[Generator]
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
        if (context.SyntaxReceiver is not ReactiveSyntaxReceiver receiver)
        {
            return;
        }

        var knownSymbols = ReactiveKnownSymbols.Create(context.Compilation);
        var classDataList = receiver.BuildClassData(context.Compilation, knownSymbols);

        // Determine which classes need virtual (have a derived class with reactive members)
        ComputeNeedsVirtual(classDataList);

        foreach (var classData in classDataList)
        {
            ProcessClass(context, classData, knownSymbols);
        }
    }

    private void ComputeNeedsVirtual(IReadOnlyList<ReactiveClassData> classDataList)
    {
        foreach (var classData in classDataList)
        {
            // A derived type may live in another assembly/Unity asmdef and therefore be invisible to this
            // generator run. Keep every inheritable reactive root extensible; sealed roots cannot be derived.
            classData.NeedsVirtual = !classData.HasReactiveBase && !classData.ClassSymbol.IsSealed;
        }
    }

    private void ProcessClass(
        GeneratorExecutionContext context,
        ReactiveClassData classData,
        ReactiveKnownSymbols knownSymbols)
    {
        var classSymbol = classData.ClassSymbol;

        // Skip generation for derived classes with no bindings
        // They simply inherit base class's ObserveChanges/ResetChanges
        if (classData.HasReactiveBase &&
            classData.Bindings.Count == 0)
        {
            return;
        }

        // Process auto-inferred bindings before validation
        ProcessAutoInferredBindings(context, classData);

        // Validate class
        if (!ValidateClass(context, classData, knownSymbols))
        {
            return;
        }

        // ReactiveBind identifies a source by name, so overloads cannot be represented unambiguously.
        var duplicateSourceGroups = classData.Sources.GroupBy(s => s.MemberName)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicateSourceGroups.Count > 0)
        {
            foreach (var group in duplicateSourceGroups)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB10015_DuplicateSourceIdentifier,
                    group.First().Location,
                    classSymbol.Name,
                    group.Key));
            }
            return;
        }

        // Match sources and bindings
        var sourceDict = classData.Sources.ToDictionary(s => s.MemberName);

        // Validate bindings (marks invalid bindings)
        ValidateBindings(context, classData, sourceDict);

        // Check for unused sources (warning) - only consider valid bindings
        CheckUnusedSources(context, classData, sourceDict);

        // Generate code (using only valid bindings)
        var code = GenerateCode(classData, sourceDict);

        var fileName = $"ReactiveBindGenerator.{GeneratorHelper.GetFullTypeName(classSymbol)}.g.cs";
        context.AddSource(fileName, code);
    }

    private void ProcessAutoInferredBindings(GeneratorExecutionContext context, ReactiveClassData classData)
    {
        // Build source name set for the analyzer
        var sourceNames = new HashSet<string>(classData.Sources.Select(s => s.MemberName));

        var compilation = context.Compilation;

        // Re-obtain class symbol from execute-phase compilation to ensure symbol compatibility
        // (receiver-phase symbols may not compare equal with execute-phase symbols in some hosts)
        var classSyntaxTree = classData.ClassDeclaration.SyntaxTree;
        var classSemanticModel = compilation.GetSemanticModel(classSyntaxTree);
        var classSymbol = classSemanticModel.GetDeclaredSymbol(classData.ClassDeclaration) as INamedTypeSymbol
                          ?? classData.ClassSymbol;

        foreach (var binding in classData.Bindings)
        {
            if (!binding.IsAutoInferred || binding.MethodSyntax == null)
            {
                continue;
            }

            // RB10024: Auto-inferred bindings must have no parameters
            if (binding.ParameterTypes.Length > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB10024_AutoInferredWithParameters,
                    binding.Location,
                    binding.MethodName));
                continue;
            }

            // Use method's own syntax tree for semantic model (handles partial classes across files)
            var methodSyntaxTree = binding.MethodSyntax.SyntaxTree;
            var semanticModel = ReferenceEquals(methodSyntaxTree, classSyntaxTree)
                ? classSemanticModel
                : compilation.GetSemanticModel(methodSyntaxTree);

            // Analyze the method body to find referenced sources
            var referencedSources = MethodBodyAnalyzer.FindReferencedSources(
                binding.MethodSyntax,
                semanticModel,
                sourceNames,
                classSymbol);

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
                    DiagnosticDescriptors.RB10023_NoSourcesInferred,
                    binding.Location,
                    binding.MethodName));
            }
        }
    }

    private bool ValidateClass(
        GeneratorExecutionContext context,
        ReactiveClassData classData,
        ReactiveKnownSymbols knownSymbols)
    {
        var classSymbol = classData.ClassSymbol;
        var classDeclaration = classData.ClassDeclaration;
        bool isValid = true;

        // RB10001: Class must be partial
        if (!GeneratorHelper.IsPartial(classSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10001_ClassNotPartial,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name));
            isValid = false;
        }

        foreach (var containingType in GeneratorHelper.GetContainingTypes(classSymbol))
        {
            if (GeneratorHelper.IsPartial(containingType)) continue;
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10001_ClassNotPartial,
                GeneratorHelper.GetIdentifierLocation(containingType, classDeclaration.Identifier.GetLocation()),
                containingType.Name));
            isValid = false;
        }

        // RB10002: Class must implement IReactiveObserver
        bool implementsInterface = GeneratorHelper.IsOrImplementsInterface(
            classSymbol, knownSymbols.IReactiveObserver);

        if (!implementsInterface)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10002_ClassNotImplementInterface,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name));
            isValid = false;
        }

        // RB10003: ReactiveThrottle must be >= 1
        if (classData.ThrottleCallCount.HasValue && classData.ThrottleCallCount.Value < 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10003_ThrottleInvalidValue,
                classDeclaration.Identifier.GetLocation(),
                classData.ThrottleCallCount.Value));
            isValid = false;
        }

        // RB10004: ReactiveThrottle without IReactiveObserver
        if (classData.ThrottleCallCount.HasValue && !implementsInterface)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10004_ThrottleWithoutInterface,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name));
            isValid = false;
        }

        // Validate ReactiveSource members
        foreach (var source in classData.Sources)
        {
            if (!ValidateSource(context, source, knownSymbols))
            {
                isValid = false;
            }
        }

        return isValid;
    }

    private bool ValidateSource(
        GeneratorExecutionContext context,
        ReactiveSourceData source,
        ReactiveKnownSymbols knownSymbols)
    {
        bool isValid = true;

        // RB10010: Method must have return type (not void)
        if (source.MemberKind == ReactiveSourceKind.Method && source.TypeSymbol.SpecialType == SpecialType.System_Void)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10010_MethodReturnsVoid,
                source.Location,
                source.MemberName));
            isValid = false;
        }

        // RB10011: Property must have getter
        if (source.MemberKind == ReactiveSourceKind.Property && !source.HasGetter)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10011_PropertyNoGetter,
                source.Location,
                source.MemberName));
            isValid = false;
        }

        // RB10012: Method must have no parameters
        if (source.MemberKind == ReactiveSourceKind.Method && source.HasParameters)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10012_MethodHasParameters,
                source.Location,
                source.MemberName));
            isValid = false;
        }

        // RB10013: Type must be primitive, struct, or IVersion
        if (!IsSupportedSourceType(source.TypeSymbol, knownSymbols))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10013_UnsupportedSourceType,
                source.Location,
                source.MemberName,
                source.TypeSymbol.ToDisplayString()));
            isValid = false;
        }

        // RB10014: Custom struct must have == operator (skip for IVersion types)
        if (IsCustomStructWithoutEqualityOperator(source.TypeSymbol, knownSymbols))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RB10014_StructMissingEqualityOperator,
                source.Location,
                source.MemberName,
                source.TypeSymbol.ToDisplayString()));
            isValid = false;
        }

        return isValid;
    }

    private bool IsCustomStructWithoutEqualityOperator(
        ITypeSymbol typeSymbol,
        ReactiveKnownSymbols knownSymbols)
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
        if (GeneratorHelper.IsOrImplementsInterface(typeSymbol, knownSymbols.IVersion))
        {
            return false;
        }

        // Check if the struct has op_Equality (== operator)
        var hasEqualityOperator = typeSymbol.GetMembers("op_Equality")
            .OfType<IMethodSymbol>()
            .Any(m => m.MethodKind == MethodKind.UserDefinedOperator);

        return !hasEqualityOperator;
    }

    private bool IsSupportedSourceType(ITypeSymbol typeSymbol, ReactiveKnownSymbols knownSymbols)
    {
        // Allow primitive, enum, nullable, and custom struct value types.
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
        if (GeneratorHelper.IsOrImplementsInterface(typeSymbol, knownSymbols.IVersion))
        {
            return true;
        }

        return false;
    }

    private void ValidateBindings(GeneratorExecutionContext context, ReactiveClassData classData,
        Dictionary<string, ReactiveSourceData> sourceDict)
    {
        foreach (var binding in classData.Bindings)
        {
            bool bindingValid = true;

            // RB10016: ReactiveBind must have at least one id
            // Skip if auto-inferred (RB10023 is already reported for failed inference)
            if (binding.ReactiveIds.Length == 0)
            {
                if (!binding.IsAutoInferred)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.RB10016_BindEmptyIds,
                        binding.Location,
                        binding.MethodName));
                }
                binding.IsValid = false;
                continue;
            }

            // RB10017: Method cannot be static
            if (binding.IsStatic)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB10017_MethodIsStatic,
                    binding.Location,
                    binding.MethodName));
                bindingValid = false;
            }

            // RB10018: Method must return void
            if (!binding.ReturnsVoid)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB10018_MethodNotVoid,
                    binding.Location,
                    binding.MethodName));
                bindingValid = false;
            }

            if (binding.HasUnsupportedSignature)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB10026_UnsupportedCallbackSignature,
                    binding.Location,
                    binding.MethodName));
                bindingValid = false;
            }

            // RB10021: Check for duplicate ids
            var duplicateIds = binding.ReactiveIds
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateIds.Count > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB10021_DuplicateIds,
                    binding.Location,
                    binding.MethodName,
                    string.Join(", ", duplicateIds)));
                bindingValid = false;
            }

            // RB10022: Parameters must use nameof()
            if (!binding.UsesNameof)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB10022_NotUsingNameof,
                    binding.Location,
                    binding.MethodName));
                bindingValid = false;
            }

            // Check if all ids have corresponding sources
            var missingIds = binding.ReactiveIds.Where(id => !sourceDict.ContainsKey(id)).ToList();
            if (missingIds.Count > 0)
            {
                // Separate missing ids into: exist as members but not marked vs truly non-existent
                var notMarkedIds = new List<string>();
                var nonExistentIds = new List<string>();

                foreach (var id in missingIds)
                {
                    if (MemberExistsInClass(classData.ClassSymbol, id))
                    {
                        notMarkedIds.Add(id);
                    }
                    else
                    {
                        nonExistentIds.Add(id);
                    }
                }

                // RB10025: Member exists but not marked with [ReactiveSource]
                foreach (var id in notMarkedIds)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.RB10025_SourceNotMarked,
                        binding.Location,
                        binding.MethodName,
                        id));
                }

                // RB10008: Truly non-existent sources
                if (nonExistentIds.Count > 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.RB10008_UnmatchedBind,
                        binding.Location,
                        binding.MethodName,
                        string.Join(", ", nonExistentIds)));
                }

                binding.IsValid = false;
                continue;
            }

            // Check if sources contain version containers
            bool anyVersion = binding.ReactiveIds.Any(id => sourceDict[id].IsVersionContainer);
            bool allNonVersion = !anyVersion;

            // RB10019: Parameter count validation
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
                    DiagnosticDescriptors.RB10019_InvalidParameterCount,
                    binding.Location,
                    binding.MethodName,
                    n,
                    paramCount,
                    signatures));
                binding.IsValid = false;
                continue;
            }

            // RB10020: Parameter types must match source types
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
                            DiagnosticDescriptors.RB10020_ParameterTypeMismatch,
                            binding.Location,
                            binding.MethodName,
                            i + 1,
                            binding.ParameterTypes[i].ToDisplayString(),
                            expectedTypes[i].ToDisplayString()));
                        bindingValid = false;
                    }
                }
            }

            binding.IsValid = bindingValid;
        }
    }

    private void CheckUnusedSources(GeneratorExecutionContext context, ReactiveClassData classData,
        Dictionary<string, ReactiveSourceData> sourceDict)
    {
        var usedIds = new HashSet<string>();
        foreach (var binding in classData.Bindings.Where(b => b.IsValid))
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
                // RB10007: Unmatched ReactiveSource
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.RB10007_UnmatchedSource,
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

        // Filter to only valid bindings for code generation
        var validBindings = classData.Bindings.Where(b => b.IsValid).ToList();

        // Collect all used source ids from valid bindings
        var usedSourceIds = validBindings
            .SelectMany(b => b.ReactiveIds)
            .Distinct()
            .Where(id => sourceDict.ContainsKey(id))
            .ToList();

        var generatedNames = new HashSet<string>(classSymbol.GetMembers().Select(m => m.Name));
        string AllocateFixedName(string preferred)
        {
            var candidate = preferred;
            int suffix = 1;
            while (!generatedNames.Add(candidate)) candidate = preferred + "_" + suffix++;
            return candidate;
        }
        var initializedName = AllocateFixedName("__reactive_initialized");
        var callCountName = AllocateFixedName("__reactive_callCount");
        var sourceTokens = AllocateSourceTokens(usedSourceIds, sourceDict, generatedNames);
        string Token(string id) => sourceTokens[id];

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable disable warnings");
        sb.AppendLine("#nullable enable annotations");
        sb.AppendLine();

        bool hasNamespace = !string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>";
        if (hasNamespace)
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        var containingTypes = GeneratorHelper.GetContainingTypes(classSymbol);
        foreach (var outerType in containingTypes)
        {
            sb.AppendLine($"    partial {GeneratorHelper.GetTypeDeclaration(outerType)}");
            foreach (var constraint in GeneratorHelper.GetTypeParameterConstraints(outerType))
                sb.AppendLine($"        {constraint}");
            sb.AppendLine("    {");
        }

        sb.AppendLine($"    partial {GeneratorHelper.GetTypeDeclaration(classSymbol)}");
        foreach (var constraint in GeneratorHelper.GetTypeParameterConstraints(classSymbol))
            sb.AppendLine($"        {constraint}");
        sb.AppendLine("    {");

        // Derived classes override; inheritable roots stay virtual for derived types in other assemblies/asmdefs.
        var methodModifier = classData.HasReactiveBase ? " override" : classData.NeedsVirtual ? " virtual" : "";

        // If no valid bindings, generate empty ObserveChanges and ResetChanges methods
        // Note: derived classes with no bindings are already skipped in ProcessClass,
        // so this path only runs for root classes (no override/base call needed)
        if (validBindings.Count == 0)
        {
            sb.AppendLine($"        public{methodModifier} void ObserveChanges()");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        public{methodModifier} void ResetChanges()");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            foreach (var _ in containingTypes)
            {
                sb.AppendLine("    }");
            }

            if (hasNamespace)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        // Generate fields
        sb.AppendLine($"        private bool {initializedName};");

        if (classData.ThrottleCallCount.HasValue && classData.ThrottleCallCount.Value > 1)
        {
            sb.AppendLine($"        private int {callCountName};");
        }

        foreach (var id in usedSourceIds)
        {
            var source = sourceDict[id];
            var token = Token(id);
            var typeName = source.TypeSymbol.ToDisplayString();
            sb.AppendLine($"        private {typeName} __reactive_{token} = default!;");
            if (source.IsVersionContainer)
            {
                // For version containers, store version number
                sb.AppendLine($"        private int __reactive_{token}_version = -1;");
            }
        }

        sb.AppendLine();

        // Generate ObserveChanges method
        sb.AppendLine($"        public{methodModifier} void ObserveChanges()");
        sb.AppendLine("        {");

        // Call base implementation first for derived classes
        if (classData.HasReactiveBase)
        {
            sb.AppendLine("            base.ObserveChanges();");
            sb.AppendLine();
        }

        // Throttle logic
        if (classData.ThrottleCallCount.HasValue && classData.ThrottleCallCount.Value > 1)
        {
            int throttle = classData.ThrottleCallCount.Value;
            sb.AppendLine($"            if ({initializedName} && ++{callCountName} < {throttle})");
            sb.AppendLine("            {");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine($"            {callCountName} = 0;");
            sb.AppendLine();
        }

        // First call initialization
        sb.AppendLine($"            if (!{initializedName})");
        sb.AppendLine("            {");
        sb.AppendLine($"                {initializedName} = true;");

        // Initialize cache variables
        foreach (var id in usedSourceIds)
        {
            var source = sourceDict[id];
            var accessor = GetSourceAccessor(source);
            var token = Token(id);
            sb.AppendLine($"                __reactive_{token} = {accessor};");
            if (source.IsVersionContainer)
            {
                sb.AppendLine($"                __reactive_{token}_version = __reactive_{token} == null ? -1 : __reactive_{token}.__Version;");
            }
        }

        // Call all valid bindings; on the first observation the current value is both old and new.
        foreach (var binding in validBindings)
        {
            var callArgs = GenerateFirstCallArguments(binding, sourceTokens);
            sb.AppendLine($"                {GeneratorHelper.EscapeIdentifier(binding.MethodName)}({callArgs});");
        }

        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();

        // Change detection logic
        // First, find which sources need change flags (used in multi-source bindings)
        var multiBindings = validBindings
            .Where(b => b.ReactiveIds.Length > 1)
            .ToList();
        var singleBindingsBySource = new Dictionary<string, List<ReactiveBindData>>();
        foreach (var binding in validBindings)
        {
            if (binding.ReactiveIds.Length != 1) continue;
            var sourceId = binding.ReactiveIds[0];
            if (!singleBindingsBySource.TryGetValue(sourceId, out var bindings))
            {
                bindings = new List<ReactiveBindData>();
                singleBindingsBySource.Add(sourceId, bindings);
            }
            bindings.Add(binding);
        }
        var sourcesNeedingFlags = new HashSet<string>(multiBindings
            .SelectMany(b => b.ReactiveIds)
            .Distinct());
        var sourcesNeedingOldValues = new HashSet<string>(validBindings
            .Where(b => b.ParameterTypes.Length == 2 * b.ReactiveIds.Length)
            .SelectMany(b => b.ReactiveIds));

        // Declare change flags and old values
        foreach (var id in usedSourceIds)
        {
            var source = sourceDict[id];
            var token = Token(id);
            if (sourcesNeedingFlags.Contains(id))
            {
                sb.AppendLine($"            bool __changed_{token} = false;");
            }
            if (!source.IsVersionContainer && sourcesNeedingOldValues.Contains(id))
            {
                var typeName = source.TypeSymbol.ToDisplayString();
                sb.AppendLine($"            {typeName} __old_{token} = __reactive_{token};");
            }
        }
        sb.AppendLine();

        // Check each source for changes and call single-source bindings
        foreach (var id in usedSourceIds)
        {
            var source = sourceDict[id];
            var accessor = GetSourceAccessor(source);
            var typeName = source.TypeSymbol.ToDisplayString();
            var token = Token(id);

            if (source.IsVersionContainer)
            {
                // Compare both object identity and version; evaluate the accessor once.
                sb.AppendLine($"            {typeName} __current_{token} = {accessor};");
                sb.AppendLine($"            var __current_{token}_version = __current_{token} == null ? -1 : __current_{token}.__Version;");
                sb.AppendLine($"            if (!object.ReferenceEquals(__current_{token}, __reactive_{token}) || __current_{token}_version != __reactive_{token}_version)");
                sb.AppendLine("            {");
                if (sourcesNeedingFlags.Contains(id))
                {
                    sb.AppendLine($"                __changed_{token} = true;");
                }
                sb.AppendLine($"                __reactive_{token} = __current_{token};");
                sb.AppendLine($"                __reactive_{token}_version = __current_{token}_version;");

                // Call single-source bindings immediately
                if (singleBindingsBySource.TryGetValue(id, out var singleBindings))
                {
                    foreach (var binding in singleBindings)
                    {
                        var callArgs = GenerateMultiSourceCallArguments(binding, sourceTokens);
                        sb.AppendLine($"                {GeneratorHelper.EscapeIdentifier(binding.MethodName)}({callArgs});");
                    }
                }

                sb.AppendLine("            }");
            }
            else
            {
                // Non-version: compare values
                // Store the current value first to avoid calling getter twice (comparison + assignment)
                sb.AppendLine($"            {typeName} __current_{token} = {accessor};");
                sb.AppendLine($"            if ({GenerateInequalityCheck($"__current_{token}", $"__reactive_{token}", source.TypeSymbol)})");

                sb.AppendLine("            {");
                if (sourcesNeedingFlags.Contains(id))
                {
                    sb.AppendLine($"                __changed_{token} = true;");
                }

                sb.AppendLine($"                __reactive_{token} = __current_{token};");

                // Call single-source bindings immediately
                if (singleBindingsBySource.TryGetValue(id, out var singleBindings))
                {
                    foreach (var binding in singleBindings)
                    {
                        var callArgs = GenerateCallArguments(binding, sourceTokens, id);
                        sb.AppendLine($"                {GeneratorHelper.EscapeIdentifier(binding.MethodName)}({callArgs});");
                    }
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
                var condition = string.Join(" || ", binding.ReactiveIds.Select(id => $"__changed_{Token(id)}"));
                sb.AppendLine($"            if ({condition})");
                sb.AppendLine("            {");

                var callArgs = GenerateMultiSourceCallArguments(binding, sourceTokens);
                sb.AppendLine($"                {GeneratorHelper.EscapeIdentifier(binding.MethodName)}({callArgs});");
                sb.AppendLine("            }");
                sb.AppendLine();
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        // Generate ResetChanges method
        sb.AppendLine($"        public{methodModifier} void ResetChanges()");
        sb.AppendLine("        {");
        if (classData.HasReactiveBase)
        {
            sb.AppendLine("            base.ResetChanges();");
        }
        sb.AppendLine($"            {initializedName} = false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        foreach (var _ in containingTypes)
        {
            sb.AppendLine("    }");
        }

        if (hasNamespace)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> AllocateSourceTokens(
        List<string> sourceIds,
        Dictionary<string, ReactiveSourceData> sourceDict,
        HashSet<string> reservedNames)
    {
        var result = new Dictionary<string, string>();
        foreach (var id in sourceIds)
        {
            string token = id;
            int suffix = 1;
            while (true)
            {
                var names = new List<string>
                {
                    $"__reactive_{token}",
                    $"__current_{token}",
                    $"__old_{token}",
                    $"__changed_{token}"
                };
                if (sourceDict[id].IsVersionContainer)
                {
                    names.Add($"__reactive_{token}_version");
                    names.Add($"__current_{token}_version");
                }

                if (names.All(n => !reservedNames.Contains(n)))
                {
                    foreach (var name in names) reservedNames.Add(name);
                    result[id] = token;
                    break;
                }
                token = id + "_" + suffix++;
            }
        }
        return result;
    }

    private string GetSourceAccessor(ReactiveSourceData source)
    {
        var memberName = GeneratorHelper.EscapeIdentifier(source.MemberName);
        return source.MemberKind == ReactiveSourceKind.Method
            ? $"{memberName}()"
            : memberName;
    }

    private string GenerateFirstCallArguments(ReactiveBindData binding, Dictionary<string, string> sourceTokens)
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
            // Every accessor has already been evaluated into its cache.
            foreach (var id in binding.ReactiveIds)
                args.Add($"__reactive_{sourceTokens[id]}");
        }
        else if (paramCount == 2 * n)
        {
            // 2N params: oldValue, newValue pairs (only for non-version sources)
            // Use cached value for both old and new (no prior value exists on first call)
            foreach (var id in binding.ReactiveIds)
            {
                var token = sourceTokens[id];
                args.Add($"__reactive_{token}");
                args.Add($"__reactive_{token}");
            }
        }

        return string.Join(", ", args);
    }

    private string GenerateCallArguments(ReactiveBindData binding, Dictionary<string, string> sourceTokens, string changedId)
    {
        int paramCount = binding.ParameterTypes.Length;

        if (paramCount == 0)
        {
            return "";
        }

        var token = sourceTokens[changedId];
        var args = new List<string>();

        if (paramCount == 1)
        {
            args.Add($"__reactive_{token}");
        }
        else if (paramCount == 2)
        {
            // Single source, oldValue and newValue (non-version only)
            args.Add($"__old_{token}");
            args.Add($"__reactive_{token}");
        }

        return string.Join(", ", args);
    }

    private string GenerateMultiSourceCallArguments(ReactiveBindData binding, Dictionary<string, string> sourceTokens)
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
            foreach (var id in binding.ReactiveIds)
                args.Add($"__reactive_{sourceTokens[id]}");
        }
        else if (paramCount == 2 * n)
        {
            // 2N params: oldValue, newValue pairs (only for non-version sources)
            foreach (var id in binding.ReactiveIds)
            {
                var token = sourceTokens[id];
                args.Add($"__old_{token}");
                args.Add($"__reactive_{token}");
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

    private static bool MemberExistsInClass(INamedTypeSymbol classSymbol, string memberName)
    {
        var type = classSymbol;
        while (type != null && type.SpecialType != SpecialType.System_Object)
        {
            var members = type.GetMembers(memberName);
            if (members.Length > 0)
            {
                return true;
            }
            type = type.BaseType;
        }
        return false;
    }

    private string GenerateInequalityCheck(string left, string right, ITypeSymbol typeSymbol)
    {
        // Float: use epsilon comparison
        if (typeSymbol.SpecialType == SpecialType.System_Single)
        {
            return $"(System.Single.IsNaN({left}) ? !System.Single.IsNaN({right}) : " +
                   $"System.Single.IsNaN({right}) || ({left} != {right} && " +
                   $"(System.Single.IsInfinity({left}) || System.Single.IsInfinity({right}) || System.Math.Abs({left} - {right}) > 1e-6f)))";
        }
        // Double: use epsilon comparison
        if (typeSymbol.SpecialType == SpecialType.System_Double)
        {
            return $"(System.Double.IsNaN({left}) ? !System.Double.IsNaN({right}) : " +
                   $"System.Double.IsNaN({right}) || ({left} != {right} && " +
                   $"(System.Double.IsInfinity({left}) || System.Double.IsInfinity({right}) || System.Math.Abs({left} - {right}) > 1e-9d)))";
        }
        // All other types: use !=
        // If struct doesn't implement == operator, compiler will report error
        return $"{left} != {right}";
    }
}
