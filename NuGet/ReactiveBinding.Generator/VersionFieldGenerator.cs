using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ReactiveBinding.Generator;

[Generator(LanguageNames.CSharp)]
public class VersionFieldGenerator : ISourceGenerator
{
    private const string IVersionInterfaceName = "ReactiveBinding.IVersion";

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new VersionFieldSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not VersionFieldSyntaxReceiver receiver)
            return;

        // Flat-registry model: a class that implements IVersionSync syncs every [VersionField];
        // each synced field gets a local slot (its index among the class's fields).
        foreach (var classData in receiver.ClassDataList)
        {
            ProcessClass(context, classData);
        }
    }

    private void ProcessClass(GeneratorExecutionContext context, VersionFieldClassData classData)
    {
        var classSymbol = classData.ClassSymbol;

        // Validate class
        if (!ValidateClass(context, classData))
            return;

        // Validate fields and filter valid ones
        var validFields = classData.Fields.Where(f => ValidateField(context, classData, f)).ToList();

        if (validFields.Count == 0)
            return;

        // Sync is opt-in at the class level: declaring `: IVersionSync` syncs every [VersionField].
        bool syncEnabled = ImplementsIVersionSync(classSymbol);
        bool syncValid = ResolveSync(context, validFields, syncEnabled);

        // Generate code
        var code = GenerateCode(classData, validFields, syncValid);
        var fileName = $"VersionFieldGenerator.{GeneratorHelper.GetFullTypeName(classSymbol)}.g.cs";
        context.AddSource(fileName, code);
    }

    /// <summary>
    /// When the class is sync-enabled (implements IVersionSync), marks every valid [VersionField] as synced,
    /// assigns slots/kinds, and reports VS2001 for unsupported field types.
    /// Returns true if sync code can be generated for this class.
    /// </summary>
    private bool ResolveSync(GeneratorExecutionContext context,
        System.Collections.Generic.List<VersionFieldData> validFields, bool syncEnabled)
    {
        if (!syncEnabled)
            return false;

        bool valid = true;

        int slot = 0;
        foreach (var f in validFields)
        {
            f.IsSynced = true;
            f.SyncSlot = slot++;
            f.SyncKind = ResolveSyncKind(f.TypeSymbol);

            // Supported: scalars, nested SyncObject, VersionList<scalar|SyncObject>,
            // VersionHashSet<scalar>, VersionDictionary<scalar,scalar>.
            bool supported = f.SyncKind == VersionSyncKind.Scalar || f.SyncKind == VersionSyncKind.SyncObject;
            if (!supported && f.SyncKind == VersionSyncKind.Container)
            {
                var ck = GetContainerInfo(f.TypeSymbol, out var key, out var elem);
                if (ck == ContainerKind.List && elem != null)
                {
                    var ek = ResolveSyncKind(elem);
                    supported = ek == VersionSyncKind.Scalar || ek == VersionSyncKind.SyncObject;
                }
                else if (ck == ContainerKind.HashSet && elem != null)
                {
                    supported = ResolveSyncKind(elem) == VersionSyncKind.Scalar;
                }
                else if (ck == ContainerKind.Dictionary && key != null && elem != null)
                {
                    supported = ResolveSyncKind(key) == VersionSyncKind.Scalar
                             && ResolveSyncKind(elem) == VersionSyncKind.Scalar;
                }
            }
            if (!supported)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.VS2001_UnsupportedSyncType,
                    f.Location, f.FieldName, f.TypeSymbol.ToDisplayString()));
                valid = false;
            }
        }

        return valid;
    }

    private bool ValidateClass(GeneratorExecutionContext context, VersionFieldClassData classData)
    {
        var classSymbol = classData.ClassSymbol;
        var classDeclaration = classData.ClassDeclaration;
        bool isValid = true;

        // VF1001: Class must be partial
        if (!classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF1001_ClassNotPartial,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name));
            isValid = false;
        }

        // VF1002: Class must implement IVersionElement
        bool implementsInterface = classSymbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == IVersionInterfaceName);

        if (!implementsInterface)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF1002_ClassNotImplementInterface,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name));
            isValid = false;
        }

        return isValid;
    }

    private bool ValidateField(GeneratorExecutionContext context,
        VersionFieldClassData classData, VersionFieldData field)
    {
        bool isValid = true;

        // VF2001: Field must have m_ prefix
        if (!field.FieldName.StartsWith("m_") || field.FieldName.Length <= 2)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF2001_FieldNotPrefixed,
                field.Location,
                field.FieldName));
            isValid = false;
        }

        // VF2002: Field must be private
        if (!field.IsPrivate)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF2002_FieldNotPrivate,
                field.Location,
                field.FieldName));
            isValid = false;
        }

        // VF2003: Check for property name collision (ignore our own generated properties)
        var existingProperty = classData.ClassSymbol.GetMembers(field.PropertyName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !IsAutoGenerated(p));

        if (existingProperty != null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF2003_PropertyAlreadyExists,
                field.Location,
                field.PropertyName,
                field.FieldName));
            isValid = false;
        }

        return isValid;
    }

    private string GenerateCode(VersionFieldClassData classData, List<VersionFieldData> fields, bool syncValid)
    {
        var sb = new StringBuilder();
        var classSymbol = classData.ClassSymbol;
        var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
        var className = classSymbol.Name;

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        bool hasNamespace = !string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>";
        if (hasNamespace)
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        var containingTypes = GeneratorHelper.GetContainingTypes(classSymbol);
        var baseIndent = "    ";
        foreach (var outerType in containingTypes)
        {
            sb.AppendLine($"{baseIndent}partial class {outerType.Name}");
            sb.AppendLine($"{baseIndent}{{");
            baseIndent += "    ";
        }

        // Interfaces (IVersion / IVersionSync) come entirely from the user's own declaration;
        // the generated partial only provides the member implementations.
        sb.AppendLine($"{baseIndent}partial class {className}");
        sb.AppendLine($"{baseIndent}{{");

        var memberIndent = baseIndent + "    ";

        sb.AppendLine($"{memberIndent}public ReactiveBinding.IVersion __Parent {{ get; set; }}");
        sb.AppendLine();
        sb.AppendLine($"{memberIndent}public int __Version {{ get; private set; }}");
        sb.AppendLine();
        sb.AppendLine($"{memberIndent}public void __IncrementVersion()");
        sb.AppendLine($"{memberIndent}{{");
        sb.AppendLine($"{memberIndent}    __Version = ReactiveBinding.VersionCounter.Next();");
        sb.AppendLine($"{memberIndent}    if (__Parent != null) __Parent.__IncrementVersion();");
        sb.AppendLine($"{memberIndent}}}");
        sb.AppendLine();

        if (syncValid)
        {
            var syncFields = fields.Where(f => f.IsSynced).OrderBy(f => f.SyncSlot).ToList();
            GenerateSyncMembers(sb, syncFields, memberIndent);
        }

        foreach (var field in fields)
        {
            GenerateProperty(sb, field, baseIndent, syncValid && field.IsSynced);
        }

        sb.AppendLine($"{baseIndent}}}");

        for (int i = 0; i < containingTypes.Count; i++)
        {
            baseIndent = baseIndent.Substring(4);
            sb.AppendLine($"{baseIndent}}}");
        }

        if (hasNamespace)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private void GenerateProperty(StringBuilder sb, VersionFieldData field, string baseIndent, bool emitSync)
    {
        var typeName = field.TypeSymbol.ToDisplayString();
        var propertyName = field.PropertyName;
        var fieldName = field.FieldName;
        var memberIndent = baseIndent + "    ";
        var bodyIndent = memberIndent + "    ";
        var innerIndent = bodyIndent + "    ";
        var deepIndent = innerIndent + "    ";

        sb.AppendLine();

        foreach (var attr in field.PropertyAttributes)
        {
            sb.AppendLine($"{memberIndent}[{attr}]");
        }

        sb.AppendLine($"{memberIndent}public {typeName} {propertyName}");
        sb.AppendLine($"{memberIndent}{{");
        sb.AppendLine($"{bodyIndent}get => {fieldName};");
        sb.AppendLine($"{bodyIndent}set");
        sb.AppendLine($"{bodyIndent}{{");

        sb.AppendLine($"{innerIndent}if ({GenerateInequalityCheck("value", fieldName, field.TypeSymbol)})");
        sb.AppendLine($"{innerIndent}{{");

        bool isContainer = emitSync && field.SyncKind == VersionSyncKind.Container;
        if (field.IsVersionType && emitSync)
        {
            // Synced reference field: assign + wire __Parent (+ container __InitSync), bump the version,
            // THEN emit the sync records — unregister the old subtree (removals), attach + emit the new
            // subtree, and write this node's ref record (or null). __old keeps the prior value for removal.
            sb.AppendLine($"{deepIndent}var __old = {fieldName};");
            sb.AppendLine($"{deepIndent}if ({fieldName} != null) {fieldName}.__Parent = null;");
            sb.AppendLine($"{deepIndent}{fieldName} = value;");
            sb.AppendLine($"{deepIndent}if (value != null)");
            sb.AppendLine($"{deepIndent}{{");
            sb.AppendLine($"{deepIndent}    value.__Parent = this;");
            if (isContainer)
                sb.AppendLine($"{deepIndent}    value.__InitSync({ContainerInitArgs(field)});");
            sb.AppendLine($"{deepIndent}}}");
            sb.AppendLine($"{deepIndent}__IncrementVersion();");
            sb.AppendLine($"{deepIndent}if (__SyncContext != null)");
            sb.AppendLine($"{deepIndent}{{");
            sb.AppendLine($"{deepIndent}    if (__old != null) __Recurse(ReactiveBinding.SyncOp.Unregister, __old);");
            sb.AppendLine($"{deepIndent}    if (value != null)");
            sb.AppendLine($"{deepIndent}    {{");
            sb.AppendLine($"{deepIndent}        __Recurse(ReactiveBinding.SyncOp.Attach, value);");
            sb.AppendLine($"{deepIndent}        var __w = __SyncContext.__Writer; __w.Write((byte)0); __w.Write(__SyncId); __w.Write((byte){field.SyncSlot}); __w.Write(value.__SyncId);");
            sb.AppendLine($"{deepIndent}        __Recurse(ReactiveBinding.SyncOp.WriteSubtree, value);");
            sb.AppendLine($"{deepIndent}    }}");
            sb.AppendLine($"{deepIndent}    else");
            sb.AppendLine($"{deepIndent}    {{");
            sb.AppendLine($"{deepIndent}        var __w = __SyncContext.__Writer; __w.Write((byte)0); __w.Write(__SyncId); __w.Write((byte){field.SyncSlot}); __w.Write(0);");
            sb.AppendLine($"{deepIndent}    }}");
            sb.AppendLine($"{deepIndent}}}");
        }
        else if (field.IsVersionType)
        {
            // Non-synced version-type field: __Parent chain only.
            sb.AppendLine($"{deepIndent}if ({fieldName} != null) {fieldName}.__Parent = null;");
            sb.AppendLine($"{deepIndent}{fieldName} = value;");
            sb.AppendLine($"{deepIndent}if (value != null) value.__Parent = this;");
            sb.AppendLine($"{deepIndent}__IncrementVersion();");
        }
        else
        {
            sb.AppendLine($"{deepIndent}{fieldName} = value;");
            sb.AppendLine($"{deepIndent}__IncrementVersion();");
            if (emitSync)   // synced scalar: write this field's record after the version bump
            {
                sb.AppendLine($"{deepIndent}if (__SyncContext != null)");
                sb.AppendLine($"{deepIndent}{{");
                sb.AppendLine($"{deepIndent}    var __w = __SyncContext.__Writer; __w.Write((byte)0); __w.Write(__SyncId); __w.Write((byte){field.SyncSlot});");
                EmitScalarWrite(sb, deepIndent + "    ", "__w", fieldName, field.TypeSymbol);
                sb.AppendLine($"{deepIndent}}}");
            }
        }
        sb.AppendLine($"{innerIndent}}}");

        sb.AppendLine($"{bodyIndent}}}");
        sb.AppendLine($"{memberIndent}}}");
    }

    private void GenerateSyncMembers(StringBuilder sb, List<VersionFieldData> syncFields, string mi)
    {
        var bi = mi + "    ";
        var ci = bi + "    ";

        sb.AppendLine($"{mi}public int __SyncId {{ get; set; }}");
        sb.AppendLine($"{mi}public ReactiveBinding.SyncContext __SyncContext {{ get; set; }}");
        sb.AppendLine();

        // AttachTo: seeding entry — register this node + its subtree into the context (no writing).
        sb.AppendLine($"{mi}public void AttachTo(ReactiveBinding.SyncContext ctx)");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}__SyncContext = ctx;");
        sb.AppendLine($"{bi}__Recurse(ReactiveBinding.SyncOp.Attach, this);");
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        EmitApplyMethod(sb, mi);
        sb.AppendLine();

        // __SyncChildren: recurse the op over each obj-like child via the inline __Recurse.
        sb.AppendLine($"{mi}public void __SyncChildren(ReactiveBinding.SyncOp op)");
        sb.AppendLine($"{mi}{{");
        foreach (var f in syncFields)
            if (IsObjLike(f))
                sb.AppendLine($"{bi}if ({f.FieldName} != null) __Recurse(op, {f.FieldName});");
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        // Full snapshot of this node: one self-contained [0][id][slot][payload] record per field.
        // Not recursive — referenced children are emitted by the WriteSubtree recursion.
        sb.AppendLine($"{mi}public void __Commit()");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}var writer = __SyncContext.__Writer;");
        foreach (var f in syncFields)
        {
            sb.AppendLine($"{bi}writer.Write((byte)0); writer.Write(__SyncId); writer.Write((byte){f.SyncSlot});");
            EmitFieldPayloadWrite(sb, bi, f);
        }
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        // Apply a single field record: [field id][payload] (the [0][id] header is already consumed).
        sb.AppendLine($"{mi}public void __Apply(System.IO.BinaryReader reader)");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}switch (reader.ReadByte())");
        sb.AppendLine($"{bi}{{");
        foreach (var f in syncFields)
            EmitFieldRead(sb, ci, f);
        sb.AppendLine($"{bi}}}");
        sb.AppendLine($"{mi}}}");

        // Element write/read delegates (scalar) or factory (object) injected into container members.
        foreach (var f in syncFields)
        {
            if (f.SyncKind != VersionSyncKind.Container) continue;
            var kind = GetContainerInfo(f.TypeSymbol, out var key, out var elem);
            if (elem == null) continue;

            if (kind == ContainerKind.Dictionary && key != null)
            {
                EmitScalarDelegate(sb, mi, bi, $"__wKey_{f.PropertyName}", $"__rKey_{f.PropertyName}", key);
                EmitScalarDelegate(sb, mi, bi, $"__wVal_{f.PropertyName}", $"__rVal_{f.PropertyName}", elem);
            }
            else if (kind == ContainerKind.List && ResolveSyncKind(elem) == VersionSyncKind.SyncObject)
            {
                var etn = elem.ToDisplayString();
                sb.AppendLine();
                sb.AppendLine($"{mi}private static ReactiveBinding.IVersionSync __new_{f.PropertyName}() => new {etn}();");
            }
            else
            {
                EmitScalarDelegate(sb, mi, bi, $"__wElem_{f.PropertyName}", $"__rElem_{f.PropertyName}", elem);
            }
        }
    }

    /// <summary>Writes one field's payload: scalar inline, or a reference field's child id.</summary>
    private void EmitFieldPayloadWrite(StringBuilder sb, string indent, VersionFieldData f)
    {
        if (IsObjLike(f))
            sb.AppendLine($"{indent}writer.Write({f.FieldName} != null ? {f.FieldName}.__SyncId : 0);");
        else
            EmitScalarWrite(sb, indent, "writer", f.FieldName, f.TypeSymbol);
    }

    /// <summary>Emits the generic register / unregister / write-subtree recursion driver (one copy per type).</summary>
    private void EmitApplyMethod(StringBuilder sb, string mi)
    {
        var bi = mi + "    ";
        var ci = bi + "    ";
        sb.AppendLine($"{mi}private void __Recurse(ReactiveBinding.SyncOp op, ReactiveBinding.IVersionSync child)");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}switch (op)");
        sb.AppendLine($"{bi}{{");
        sb.AppendLine($"{ci}case ReactiveBinding.SyncOp.Attach:");
        sb.AppendLine($"{ci}    if (child.__SyncId == 0)");
        sb.AppendLine($"{ci}    {{");
        sb.AppendLine($"{ci}        child.__SyncId = __SyncContext.__NextId++;");
        sb.AppendLine($"{ci}        child.__SyncContext = __SyncContext;");
        sb.AppendLine($"{ci}        __SyncContext.__Objects[child.__SyncId] = child;");
        sb.AppendLine($"{ci}        child.__SyncChildren(ReactiveBinding.SyncOp.Attach);");
        sb.AppendLine($"{ci}    }}");
        sb.AppendLine($"{ci}    break;");
        sb.AppendLine($"{ci}case ReactiveBinding.SyncOp.Unregister:");
        sb.AppendLine($"{ci}    if (child.__SyncId != 0)");
        sb.AppendLine($"{ci}    {{");
        sb.AppendLine($"{ci}        var __w = __SyncContext.__Writer;");
        sb.AppendLine($"{ci}        __SyncContext.__Objects.Remove(child.__SyncId);");
        sb.AppendLine($"{ci}        __w.Write((byte)1); __w.Write(child.__SyncId);");
        sb.AppendLine($"{ci}        child.__SyncChildren(ReactiveBinding.SyncOp.Unregister);");
        sb.AppendLine($"{ci}        child.__SyncId = 0; child.__SyncContext = null;");
        sb.AppendLine($"{ci}    }}");
        sb.AppendLine($"{ci}    break;");
        sb.AppendLine($"{ci}case ReactiveBinding.SyncOp.WriteSubtree:");
        sb.AppendLine($"{ci}    child.__Commit();");
        sb.AppendLine($"{ci}    child.__SyncChildren(ReactiveBinding.SyncOp.WriteSubtree);");
        sb.AppendLine($"{ci}    break;");
        sb.AppendLine($"{bi}}}");
        sb.AppendLine($"{mi}}}");
    }

    /// <summary>Emits the __Apply switch case for one field (reference cases resolve a node inline).</summary>
    private void EmitFieldRead(StringBuilder sb, string ci, VersionFieldData f)
    {
        if (!IsObjLike(f))
        {
            sb.AppendLine($"{ci}case {f.SyncSlot}: {f.FieldName} = {ScalarReadExpr("reader", f.TypeSymbol)}; break;");
            return;
        }

        var tn = f.TypeSymbol.ToDisplayString();
        sb.AppendLine($"{ci}case {f.SyncSlot}:");
        sb.AppendLine($"{ci}{{");
        sb.AppendLine($"{ci}    int __id = reader.ReadInt32();");
        sb.AppendLine($"{ci}    if (__id == 0) {f.FieldName} = null;");
        sb.AppendLine($"{ci}    else if (__SyncContext.__Objects.TryGetValue(__id, out var __n)) {f.FieldName} = ({tn})__n;");
        sb.AppendLine($"{ci}    else {{ var __c = new {tn}(); __c.__SyncId = __id; __c.__SyncContext = __SyncContext; __SyncContext.__Objects[__id] = __c; {f.FieldName} = __c; }}");
        if (f.SyncKind == VersionSyncKind.Container)
            sb.AppendLine($"{ci}    if ({f.FieldName} != null) {{ {f.FieldName}.__Parent = this; {f.FieldName}.__InitSync({ContainerInitArgs(f)}); }}");
        else
            sb.AppendLine($"{ci}    if ({f.FieldName} != null) {f.FieldName}.__Parent = this;");
        sb.AppendLine($"{ci}    break;");
        sb.AppendLine($"{ci}}}");
    }

    private void EmitScalarDelegate(StringBuilder sb, string mi, string bi, string wName, string rName, ITypeSymbol t)
    {
        var tn = t.ToDisplayString();
        sb.AppendLine();
        sb.AppendLine($"{mi}private static void {wName}(System.IO.BinaryWriter writer, {tn} e)");
        sb.AppendLine($"{mi}{{");
        EmitScalarWrite(sb, bi, "writer", "e", t);
        sb.AppendLine($"{mi}}}");
        sb.AppendLine($"{mi}private static {tn} {rName}(System.IO.BinaryReader reader) {{ return {ScalarReadExpr("reader", t)}; }}");
    }

    private static bool IsObjLike(VersionFieldData f)
        => f.SyncKind == VersionSyncKind.SyncObject || f.SyncKind == VersionSyncKind.Container;

    private static string ContainerInitArgs(VersionFieldData f)
    {
        var kind = GetContainerInfo(f.TypeSymbol, out _, out var elem);
        if (kind == ContainerKind.Dictionary)
            return $"__wKey_{f.PropertyName}, __rKey_{f.PropertyName}, __wVal_{f.PropertyName}, __rVal_{f.PropertyName}";
        if (kind == ContainerKind.List && elem != null && ResolveSyncKind(elem) == VersionSyncKind.SyncObject)
            return $"__new_{f.PropertyName}";   // object elements: inject element factory
        return $"__wElem_{f.PropertyName}, __rElem_{f.PropertyName}";   // scalar elements: inject delegates
    }

    private static string ScalarReadExpr(string r, ITypeSymbol t)
    {
        if (t.SpecialType == SpecialType.System_String)
            return $"{r}.ReadBoolean() ? {r}.ReadString() : null";
        if (t.TypeKind == TypeKind.Enum)
        {
            var u = ((INamedTypeSymbol)t).EnumUnderlyingType!.SpecialType;
            return $"({t.ToDisplayString()})({r}.{GetReaderMethod(u)}())";
        }
        return $"{r}.{GetReaderMethod(t.SpecialType)}()";
    }

    private enum ContainerKind { None, List, Dictionary, HashSet }

    private static ContainerKind GetContainerInfo(ITypeSymbol t, out ITypeSymbol? key, out ITypeSymbol? value)
    {
        key = null;
        value = null;
        if (t is not INamedTypeSymbol named || !named.IsGenericType) return ContainerKind.None;
        var def = named.ConstructedFrom.ToDisplayString();
        if (def == "ReactiveBinding.VersionList<T>") { value = named.TypeArguments[0]; return ContainerKind.List; }
        if (def == "ReactiveBinding.VersionHashSet<T>") { value = named.TypeArguments[0]; return ContainerKind.HashSet; }
        if (def == "ReactiveBinding.VersionDictionary<TKey, TValue>") { key = named.TypeArguments[0]; value = named.TypeArguments[1]; return ContainerKind.Dictionary; }
        return ContainerKind.None;
    }

    private void EmitScalarWrite(StringBuilder sb, string indent, string w, string access, ITypeSymbol t)
    {
        if (t.SpecialType == SpecialType.System_String)
        {
            sb.AppendLine($"{indent}{w}.Write({access} != null);");
            sb.AppendLine($"{indent}if ({access} != null) {w}.Write({access});");
        }
        else if (t.TypeKind == TypeKind.Enum)
        {
            var underlying = ((INamedTypeSymbol)t).EnumUnderlyingType!.ToDisplayString();
            sb.AppendLine($"{indent}{w}.Write(({underlying}){access});");
        }
        else
        {
            sb.AppendLine($"{indent}{w}.Write({access});");
        }
    }

    private static VersionSyncKind ResolveSyncKind(ITypeSymbol t)
    {
        if (IsScalar(t)) return VersionSyncKind.Scalar;
        if (IsVersionContainer(t)) return VersionSyncKind.Container;
        if (t is INamedTypeSymbol named && t.TypeKind == TypeKind.Class && !named.IsAbstract && ImplementsIVersionSync(named))
            return VersionSyncKind.SyncObject;
        return VersionSyncKind.None;
    }

    private static bool IsScalar(ITypeSymbol t)
        => t.TypeKind == TypeKind.Enum || GetReaderMethod(t.SpecialType) != null;

    private static string? GetReaderMethod(SpecialType st) => st switch
    {
        SpecialType.System_Boolean => "ReadBoolean",
        SpecialType.System_Byte => "ReadByte",
        SpecialType.System_SByte => "ReadSByte",
        SpecialType.System_Int16 => "ReadInt16",
        SpecialType.System_UInt16 => "ReadUInt16",
        SpecialType.System_Int32 => "ReadInt32",
        SpecialType.System_UInt32 => "ReadUInt32",
        SpecialType.System_Int64 => "ReadInt64",
        SpecialType.System_UInt64 => "ReadUInt64",
        SpecialType.System_Single => "ReadSingle",
        SpecialType.System_Double => "ReadDouble",
        SpecialType.System_Char => "ReadChar",
        SpecialType.System_Decimal => "ReadDecimal",
        SpecialType.System_String => "ReadString",
        _ => null
    };

    private static bool IsVersionContainer(ITypeSymbol t)
    {
        if (t is not INamedTypeSymbol named || !named.IsGenericType) return false;
        var def = named.ConstructedFrom.ToDisplayString();
        return def == "ReactiveBinding.VersionList<T>"
            || def == "ReactiveBinding.VersionDictionary<TKey, TValue>"
            || def == "ReactiveBinding.VersionHashSet<T>";
    }

    private static bool ImplementsIVersionSync(ITypeSymbol t)
        => t.AllInterfaces.Any(i => i.ToDisplayString() == "ReactiveBinding.IVersionSync");

    private static bool IsAutoGenerated(ISymbol symbol)
    {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var filePath = syntaxRef.SyntaxTree.FilePath;
            if (filePath != null && filePath.Contains("VersionFieldGenerator") && filePath.EndsWith(".g.cs"))
                return true;
        }
        return false;
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
        // All types (value and reference): use !=
        return $"{left} != {right}";
    }
}
