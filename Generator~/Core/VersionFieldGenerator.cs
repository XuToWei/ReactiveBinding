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

        // Resolve synchronization (slots, kinds, diagnostics)
        bool syncValid = ResolveSync(context, classData, validFields);

        // Generate code
        var code = GenerateCode(classData, validFields, syncValid);
        var fileName = $"VersionFieldGenerator.{GeneratorHelper.GetFullTypeName(classSymbol)}.g.cs";
        context.AddSource(fileName, code);
    }

    /// <summary>
    /// Assigns sync slots/kinds to [VersionSync] fields and reports VS1001/VS2001.
    /// Returns true if sync code can be generated for this class.
    /// </summary>
    private bool ResolveSync(GeneratorExecutionContext context,
        VersionFieldClassData classData, System.Collections.Generic.List<VersionFieldData> validFields)
    {
        if (!classData.IsSyncEnabled)
            return false;

        var syncFields = validFields.Where(f => f.IsSynced).ToList();
        bool valid = true;

        if (syncFields.Count > 64)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VS1001_TooManySyncFields,
                classData.ClassDeclaration.Identifier.GetLocation(),
                classData.ClassSymbol.Name, syncFields.Count));
            valid = false;
        }

        int slot = 0;
        foreach (var f in syncFields)
        {
            f.SyncSlot = slot++;
            f.SyncKind = ResolveSyncKind(f.TypeSymbol);

            bool unsupported = f.SyncKind == VersionSyncKind.None
                || (f.SyncKind == VersionSyncKind.Container && !IsSupportedContainer(f.TypeSymbol));
            if (unsupported)
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

        if (syncValid)
            sb.AppendLine($"{baseIndent}partial class {className} : ReactiveBinding.IVersionSyncable");
        else
            sb.AppendLine($"{baseIndent}partial class {className}");
        sb.AppendLine($"{baseIndent}{{");

        var memberIndent = baseIndent + "    ";

        sb.AppendLine($"{memberIndent}private int __version;");
        sb.AppendLine();
        sb.AppendLine($"{memberIndent}public ReactiveBinding.IVersion Parent {{ get; set; }}");
        sb.AppendLine();
        sb.AppendLine($"{memberIndent}public int Version => __version;");
        sb.AppendLine();
        sb.AppendLine($"{memberIndent}public void IncrementVersion()");
        sb.AppendLine($"{memberIndent}{{");
        sb.AppendLine($"{memberIndent}    __version = ReactiveBinding.VersionCounter.Next();");
        sb.AppendLine($"{memberIndent}    if (Parent != null) Parent.IncrementVersion();");
        sb.AppendLine($"{memberIndent}}}");
        sb.AppendLine();

        foreach (var field in fields)
        {
            GenerateProperty(sb, field, baseIndent, syncValid && field.IsSynced);
        }

        if (syncValid)
        {
            var syncFields = fields.Where(f => f.IsSynced).OrderBy(f => f.SyncSlot).ToList();
            GenerateSyncMembers(sb, syncFields, memberIndent);
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

    private void GenerateProperty(StringBuilder sb, VersionFieldData field, string baseIndent, bool emitDirty)
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

        if (field.IsVersionType)
        {
            sb.AppendLine($"{deepIndent}if ({fieldName} != null) {fieldName}.Parent = null;");
            sb.AppendLine($"{deepIndent}{fieldName} = value;");
            sb.AppendLine($"{deepIndent}if (value != null) value.Parent = this;");
        }
        else
        {
            sb.AppendLine($"{deepIndent}{fieldName} = value;");
        }

        sb.AppendLine($"{deepIndent}IncrementVersion();");
        if (emitDirty)
        {
            sb.AppendLine($"{deepIndent}__syncDirty |= (1UL << {field.SyncSlot});");
        }
        sb.AppendLine($"{innerIndent}}}");

        sb.AppendLine($"{bodyIndent}}}");
        sb.AppendLine($"{memberIndent}}}");
    }

    private void GenerateSyncMembers(StringBuilder sb, List<VersionFieldData> syncFields, string mi)
    {
        var bi = mi + "    ";   // body indent
        var ii = bi + "    ";   // inner indent

        sb.AppendLine();
        sb.AppendLine($"{mi}private ulong __syncDirty;");
        sb.AppendLine($"{mi}private int __syncWatermark;");
        sb.AppendLine($"{mi}private byte[] __syncBaseline;");
        sb.AppendLine();

        // ---- IVersionSyncable public API ----
        sb.AppendLine($"{mi}public void Commit()");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}using (var __ms = new System.IO.MemoryStream())");
        sb.AppendLine($"{bi}using (var __bw = new System.IO.BinaryWriter(__ms))");
        sb.AppendLine($"{bi}{{");
        sb.AppendLine($"{ii}WriteFull(__bw);");
        sb.AppendLine($"{ii}__bw.Flush();");
        sb.AppendLine($"{ii}__syncBaseline = __ms.ToArray();");
        sb.AppendLine($"{bi}}}");
        sb.AppendLine($"{bi}ResetSync();");
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        sb.AppendLine($"{mi}public byte[] GetFull()");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}if (__syncBaseline == null) Commit();");
        sb.AppendLine($"{bi}return __syncBaseline;");
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        sb.AppendLine($"{mi}public byte[] GetDelta()");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}if (__syncBaseline == null) Commit();");
        sb.AppendLine($"{bi}using (var __ms = new System.IO.MemoryStream())");
        sb.AppendLine($"{bi}using (var __bw = new System.IO.BinaryWriter(__ms))");
        sb.AppendLine($"{bi}{{");
        sb.AppendLine($"{ii}WriteDelta(__bw);");
        sb.AppendLine($"{ii}__bw.Flush();");
        sb.AppendLine($"{ii}return __ms.ToArray();");
        sb.AppendLine($"{bi}}}");
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        sb.AppendLine($"{mi}public void Apply(byte[] full)");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}using (var __ms = new System.IO.MemoryStream(full))");
        sb.AppendLine($"{bi}using (var __br = new System.IO.BinaryReader(__ms))");
        sb.AppendLine($"{ii}ReadFull(__br);");
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        sb.AppendLine($"{mi}public void ApplyDelta(byte[] delta)");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}using (var __ms = new System.IO.MemoryStream(delta))");
        sb.AppendLine($"{bi}using (var __br = new System.IO.BinaryReader(__ms))");
        sb.AppendLine($"{ii}ReadDelta(__br);");
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        // ---- ResetSync ----
        sb.AppendLine($"{mi}public void ResetSync()");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}__syncDirty = 0;");
        sb.AppendLine($"{bi}__syncWatermark = ReactiveBinding.VersionCounter.Current;");
        foreach (var f in syncFields)
        {
            if (f.SyncKind == VersionSyncKind.SyncObject || f.SyncKind == VersionSyncKind.Container)
                sb.AppendLine($"{bi}if ({f.FieldName} != null) {f.FieldName}.ResetSync();");
        }
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        // ---- WriteFull ----
        sb.AppendLine($"{mi}public void WriteFull(System.IO.BinaryWriter __w)");
        sb.AppendLine($"{mi}{{");
        foreach (var f in syncFields)
        {
            if (f.SyncKind == VersionSyncKind.Scalar)
                EmitScalarWrite(sb, bi, "__w", f.FieldName, f.TypeSymbol);
            else if (f.SyncKind == VersionSyncKind.SyncObject)
            {
                sb.AppendLine($"{bi}__w.Write({f.FieldName} != null);");
                sb.AppendLine($"{bi}if ({f.FieldName} != null) {f.FieldName}.WriteFull(__w);");
            }
            else // Container
            {
                sb.AppendLine($"{bi}__w.Write({f.FieldName} != null);");
                sb.AppendLine($"{bi}if ({f.FieldName} != null) {f.FieldName}.WriteFull(__w, {WArgsFull(f)});");
            }
        }
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        // ---- ReadFull ----
        sb.AppendLine($"{mi}public void ReadFull(System.IO.BinaryReader __r)");
        sb.AppendLine($"{mi}{{");
        foreach (var f in syncFields)
        {
            if (f.SyncKind == VersionSyncKind.Scalar)
                EmitScalarReadInto(sb, bi, "__r", f.FieldName, f.TypeSymbol);
            else if (f.SyncKind == VersionSyncKind.SyncObject)
                EmitSyncObjectReadFull(sb, bi, "__r", f.FieldName, f.TypeSymbol.ToDisplayString());
            else // Container
                EmitContainerReadFull(sb, bi, f);
        }
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        // ---- WriteDelta ----
        sb.AppendLine($"{mi}public void WriteDelta(System.IO.BinaryWriter __w)");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}ulong __mask = 0;");
        foreach (var f in syncFields)
        {
            if (f.SyncKind == VersionSyncKind.Scalar)
                sb.AppendLine($"{bi}if ((__syncDirty & (1UL << {f.SyncSlot})) != 0) __mask |= (1UL << {f.SyncSlot});");
            else
                sb.AppendLine($"{bi}if ((__syncDirty & (1UL << {f.SyncSlot})) != 0 || ({f.FieldName} != null && {f.FieldName}.Version > __syncWatermark)) __mask |= (1UL << {f.SyncSlot});");
        }
        sb.AppendLine($"{bi}__w.Write(__mask);");
        foreach (var f in syncFields)
        {
            sb.AppendLine($"{bi}if ((__mask & (1UL << {f.SyncSlot})) != 0)");
            sb.AppendLine($"{bi}{{");
            if (f.SyncKind == VersionSyncKind.Scalar)
            {
                EmitScalarWrite(sb, ii, "__w", f.FieldName, f.TypeSymbol);
            }
            else if (f.SyncKind == VersionSyncKind.SyncObject)
            {
                sb.AppendLine($"{ii}if ((__syncDirty & (1UL << {f.SyncSlot})) != 0)");
                sb.AppendLine($"{ii}{{");
                sb.AppendLine($"{ii}    __w.Write((byte)0);");
                sb.AppendLine($"{ii}    __w.Write({f.FieldName} != null);");
                sb.AppendLine($"{ii}    if ({f.FieldName} != null) {f.FieldName}.WriteFull(__w);");
                sb.AppendLine($"{ii}}}");
                sb.AppendLine($"{ii}else");
                sb.AppendLine($"{ii}{{");
                sb.AppendLine($"{ii}    __w.Write((byte)1);");
                sb.AppendLine($"{ii}    {f.FieldName}.WriteDelta(__w);");
                sb.AppendLine($"{ii}}}");
            }
            else // Container
            {
                sb.AppendLine($"{ii}if ((__syncDirty & (1UL << {f.SyncSlot})) != 0)");
                sb.AppendLine($"{ii}{{");
                sb.AppendLine($"{ii}    __w.Write((byte)0);");
                sb.AppendLine($"{ii}    __w.Write({f.FieldName} != null);");
                sb.AppendLine($"{ii}    if ({f.FieldName} != null) {f.FieldName}.WriteFull(__w, {WArgsFull(f)});");
                sb.AppendLine($"{ii}}}");
                sb.AppendLine($"{ii}else");
                sb.AppendLine($"{ii}{{");
                sb.AppendLine($"{ii}    __w.Write((byte)1);");
                sb.AppendLine($"{ii}    {f.FieldName}.WriteDelta(__w, {WArgsDelta(f)});");
                sb.AppendLine($"{ii}}}");
            }
            sb.AppendLine($"{bi}}}");
        }
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();

        // ---- ReadDelta ----
        sb.AppendLine($"{mi}public void ReadDelta(System.IO.BinaryReader __r)");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}ulong __mask = __r.ReadUInt64();");
        foreach (var f in syncFields)
        {
            sb.AppendLine($"{bi}if ((__mask & (1UL << {f.SyncSlot})) != 0)");
            sb.AppendLine($"{bi}{{");
            if (f.SyncKind == VersionSyncKind.Scalar)
            {
                EmitScalarReadInto(sb, ii, "__r", f.FieldName, f.TypeSymbol);
            }
            else if (f.SyncKind == VersionSyncKind.SyncObject)
            {
                var tn = f.TypeSymbol.ToDisplayString();
                sb.AppendLine($"{ii}byte __tag = __r.ReadByte();");
                sb.AppendLine($"{ii}if (__tag == 0)");
                sb.AppendLine($"{ii}{{");
                EmitSyncObjectReadFull(sb, ii + "    ", "__r", f.FieldName, tn);
                sb.AppendLine($"{ii}}}");
                sb.AppendLine($"{ii}else");
                sb.AppendLine($"{ii}{{");
                sb.AppendLine($"{ii}    if ({f.FieldName} == null) {{ {f.FieldName} = new {tn}(); {f.FieldName}.Parent = this; }}");
                sb.AppendLine($"{ii}    {f.FieldName}.ReadDelta(__r);");
                sb.AppendLine($"{ii}}}");
            }
            else // Container
            {
                var tn = f.TypeSymbol.ToDisplayString();
                sb.AppendLine($"{ii}byte __tag = __r.ReadByte();");
                sb.AppendLine($"{ii}if (__tag == 0)");
                sb.AppendLine($"{ii}{{");
                sb.AppendLine($"{ii}    if (__r.ReadBoolean())");
                sb.AppendLine($"{ii}    {{");
                sb.AppendLine($"{ii}        if ({f.FieldName} == null) {{ {f.FieldName} = new {tn}(); {f.FieldName}.Parent = this; }}");
                sb.AppendLine($"{ii}        {f.FieldName}.ReadFull(__r, {RArgsFull(f)});");
                sb.AppendLine($"{ii}    }}");
                sb.AppendLine($"{ii}    else");
                sb.AppendLine($"{ii}    {{");
                sb.AppendLine($"{ii}        if ({f.FieldName} != null) {f.FieldName}.Parent = null;");
                sb.AppendLine($"{ii}        {f.FieldName} = null;");
                sb.AppendLine($"{ii}    }}");
                sb.AppendLine($"{ii}}}");
                sb.AppendLine($"{ii}else");
                sb.AppendLine($"{ii}{{");
                sb.AppendLine($"{ii}    if ({f.FieldName} == null) {{ {f.FieldName} = new {tn}(); {f.FieldName}.Parent = this; }}");
                sb.AppendLine($"{ii}    {f.FieldName}.ReadDelta(__r, {RArgsDelta(f)});");
                sb.AppendLine($"{ii}}}");
            }
            sb.AppendLine($"{bi}}}");
        }
        sb.AppendLine($"{mi}}}");

        // ---- per-container element/key serializer helpers ----
        foreach (var f in syncFields)
        {
            var ck = GetContainerInfo(f.TypeSymbol, out var key, out var value);
            if (ck == ContainerKind.None || value == null) continue;
            var vk = ResolveSyncKind(value);
            sb.AppendLine();
            EmitWriterHelper(sb, mi, $"__wV_s{f.SyncSlot}", value, vk);
            sb.AppendLine();
            EmitReaderHelper(sb, mi, $"__rV_s{f.SyncSlot}", value, vk);
            // Element field-level patch helpers (SyncObject element values only; not for HashSet).
            if (vk == VersionSyncKind.SyncObject && ck != ContainerKind.HashSet)
            {
                var vtn = value.ToDisplayString();
                sb.AppendLine();
                sb.AppendLine($"{mi}private static void __wpV_s{f.SyncSlot}(System.IO.BinaryWriter __w, {vtn} __e)");
                sb.AppendLine($"{mi}{{ __e.WriteDelta(__w); }}");
                sb.AppendLine();
                sb.AppendLine($"{mi}private static void __rpV_s{f.SyncSlot}(System.IO.BinaryReader __r, {vtn} __e)");
                sb.AppendLine($"{mi}{{ __e.ReadDelta(__r); }}");
            }
            if (key != null)
            {
                sb.AppendLine();
                EmitWriterHelper(sb, mi, $"__wK_s{f.SyncSlot}", key, VersionSyncKind.Scalar);
                sb.AppendLine();
                EmitReaderHelper(sb, mi, $"__rK_s{f.SyncSlot}", key, VersionSyncKind.Scalar);
            }
        }
    }

    private static string WArgsFull(VersionFieldData f)
    {
        GetContainerInfo(f.TypeSymbol, out var key, out _);
        return key != null ? $"__wK_s{f.SyncSlot}, __wV_s{f.SyncSlot}" : $"__wV_s{f.SyncSlot}";
    }

    private static string RArgsFull(VersionFieldData f)
    {
        GetContainerInfo(f.TypeSymbol, out var key, out _);
        return key != null ? $"__rK_s{f.SyncSlot}, __rV_s{f.SyncSlot}" : $"__rV_s{f.SyncSlot}";
    }

    private static string WArgsDelta(VersionFieldData f)
    {
        var kind = GetContainerInfo(f.TypeSymbol, out var key, out var value);
        if (kind == ContainerKind.HashSet) return $"__wV_s{f.SyncSlot}"; // no element patch
        var patch = value != null && ResolveSyncKind(value) == VersionSyncKind.SyncObject ? $"__wpV_s{f.SyncSlot}" : "null";
        return key != null
            ? $"__wK_s{f.SyncSlot}, __wV_s{f.SyncSlot}, {patch}"
            : $"__wV_s{f.SyncSlot}, {patch}";
    }

    private static string RArgsDelta(VersionFieldData f)
    {
        var kind = GetContainerInfo(f.TypeSymbol, out var key, out var value);
        if (kind == ContainerKind.HashSet) return $"__rV_s{f.SyncSlot}"; // no element patch
        var patch = value != null && ResolveSyncKind(value) == VersionSyncKind.SyncObject ? $"__rpV_s{f.SyncSlot}" : "null";
        return key != null
            ? $"__rK_s{f.SyncSlot}, __rV_s{f.SyncSlot}, {patch}"
            : $"__rV_s{f.SyncSlot}, {patch}";
    }

    private void EmitContainerReadFull(StringBuilder sb, string indent, VersionFieldData f)
    {
        var tn = f.TypeSymbol.ToDisplayString();
        sb.AppendLine($"{indent}if (__r.ReadBoolean())");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    if ({f.FieldName} == null) {{ {f.FieldName} = new {tn}(); {f.FieldName}.Parent = this; }}");
        sb.AppendLine($"{indent}    {f.FieldName}.ReadFull(__r, {RArgsFull(f)});");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}else");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    if ({f.FieldName} != null) {f.FieldName}.Parent = null;");
        sb.AppendLine($"{indent}    {f.FieldName} = null;");
        sb.AppendLine($"{indent}}}");
    }

    private void EmitWriterHelper(StringBuilder sb, string mi, string name, ITypeSymbol t, VersionSyncKind kind)
    {
        var tn = t.ToDisplayString();
        sb.AppendLine($"{mi}private static void {name}(System.IO.BinaryWriter __w, {tn} __e)");
        sb.AppendLine($"{mi}{{");
        if (kind == VersionSyncKind.SyncObject)
        {
            sb.AppendLine($"{mi}    __w.Write(__e != null);");
            sb.AppendLine($"{mi}    if (__e != null) __e.WriteFull(__w);");
        }
        else
        {
            EmitScalarWrite(sb, mi + "    ", "__w", "__e", t);
        }
        sb.AppendLine($"{mi}}}");
    }

    private void EmitReaderHelper(StringBuilder sb, string mi, string name, ITypeSymbol t, VersionSyncKind kind)
    {
        var tn = t.ToDisplayString();
        sb.AppendLine($"{mi}private static {tn} {name}(System.IO.BinaryReader __r)");
        sb.AppendLine($"{mi}{{");
        if (kind == VersionSyncKind.SyncObject)
        {
            sb.AppendLine($"{mi}    if (!__r.ReadBoolean()) return null;");
            sb.AppendLine($"{mi}    var __o = new {tn}();");
            sb.AppendLine($"{mi}    __o.ReadFull(__r);");
            sb.AppendLine($"{mi}    return __o;");
        }
        else
        {
            sb.AppendLine($"{mi}    return {ScalarReadExpr("__r", t)};");
        }
        sb.AppendLine($"{mi}}}");
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

    private static bool IsSupportedContainer(ITypeSymbol t)
    {
        var kind = GetContainerInfo(t, out var key, out var value);
        if (kind == ContainerKind.None || value == null) return false;
        var vk = ResolveSyncKind(value);
        if (vk != VersionSyncKind.Scalar && vk != VersionSyncKind.SyncObject) return false;
        if (kind == ContainerKind.Dictionary && (key == null || !IsScalar(key))) return false;
        return true;
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

    private void EmitScalarReadInto(StringBuilder sb, string indent, string r, string target, ITypeSymbol t)
    {
        if (t.SpecialType == SpecialType.System_String)
        {
            sb.AppendLine($"{indent}{target} = {r}.ReadBoolean() ? {r}.ReadString() : null;");
        }
        else if (t.TypeKind == TypeKind.Enum)
        {
            var underlying = ((INamedTypeSymbol)t).EnumUnderlyingType!;
            sb.AppendLine($"{indent}{target} = ({t.ToDisplayString()})({r}.{GetReaderMethod(underlying.SpecialType)}());");
        }
        else
        {
            sb.AppendLine($"{indent}{target} = {r}.{GetReaderMethod(t.SpecialType)}();");
        }
    }

    private void EmitSyncObjectReadFull(StringBuilder sb, string indent, string r, string target, string typeName)
    {
        sb.AppendLine($"{indent}if ({r}.ReadBoolean())");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    if ({target} == null) {{ {target} = new {typeName}(); {target}.Parent = this; }}");
        sb.AppendLine($"{indent}    {target}.ReadFull({r});");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}else");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    if ({target} != null) {target}.Parent = null;");
        sb.AppendLine($"{indent}    {target} = null;");
        sb.AppendLine($"{indent}}}");
    }

    private static VersionSyncKind ResolveSyncKind(ITypeSymbol t)
    {
        if (IsScalar(t)) return VersionSyncKind.Scalar;
        if (IsVersionContainer(t)) return VersionSyncKind.Container;
        if (t is INamedTypeSymbol named && t.TypeKind == TypeKind.Class && !named.IsAbstract && HasSyncField(named))
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

    private static bool HasSyncField(INamedTypeSymbol named)
    {
        var t = named;
        while (t != null && t.SpecialType != SpecialType.System_Object)
        {
            foreach (var m in t.GetMembers())
            {
                if (m is IFieldSymbol f && f.GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == "ReactiveBinding.VersionSyncAttribute"))
                    return true;
            }
            t = t.BaseType;
        }
        return false;
    }

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
