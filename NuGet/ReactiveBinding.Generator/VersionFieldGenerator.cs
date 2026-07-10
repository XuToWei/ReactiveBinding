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
        bool syncValid = ResolveSync(context, classSymbol, validFields, syncEnabled);

        // Generate code
        var code = GenerateCode(classData, validFields, syncValid);
        var fileName = $"VersionFieldGenerator.{GeneratorHelper.GetFullTypeName(classSymbol)}.g.cs";
        context.AddSource(fileName, code);
    }

    /// <summary>
    /// When the class is sync-enabled (implements IVersionSync), marks every valid [VersionField] as synced,
    /// assigns slots/kinds, and reports VS0001 for unsupported field types.
    /// Returns true if sync code can be generated for this class.
    /// </summary>
    private bool ResolveSync(GeneratorExecutionContext context, INamedTypeSymbol classSymbol,
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

            // Supported: scalars, nested SyncObject, VersionSyncList<scalar|SyncObject>,
            // VersionSyncHashSet<scalar|SyncObject>, VersionSyncDictionary<scalar,scalar|SyncObject>.
            bool supported = f.SyncKind == VersionSyncKind.Scalar || f.SyncKind == VersionSyncKind.SyncObject;
            if (!supported && f.SyncKind == VersionSyncKind.Container)
            {
                var ck = GetContainerInfo(f.TypeSymbol, out var key, out var elem);
                if ((ck == ContainerKind.List || ck == ContainerKind.HashSet) && elem != null)
                {
                    var ek = ResolveSyncKind(elem);
                    supported = ek == VersionSyncKind.Scalar || ek == VersionSyncKind.SyncObject;
                }
                else if (ck == ContainerKind.Dictionary && key != null && elem != null)
                {
                    var vk = ResolveSyncKind(elem);
                    supported = ResolveSyncKind(key) == VersionSyncKind.Scalar
                             && (vk == VersionSyncKind.Scalar || vk == VersionSyncKind.SyncObject);
                }
            }
            if (supported)
            {
                if (!ValidateSyncConstructors(context, f))
                    valid = false;
            }
            else
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.VS0001_UnsupportedSyncType,
                    f.Location, f.FieldName, f.TypeSymbol.ToDisplayString()));
                valid = false;
            }
        }

        return valid;
    }

    private bool ValidateSyncConstructors(GeneratorExecutionContext context, VersionFieldData field)
    {
        if (field.SyncKind == VersionSyncKind.SyncObject && !HasPublicParameterlessConstructor(field.TypeSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VS0002_SyncTypeMissingPublicParameterlessConstructor,
                field.Location, field.TypeSymbol.ToDisplayString(), field.FieldName));
            return false;
        }

        if (field.SyncKind == VersionSyncKind.Container)
        {
            var kind = GetContainerInfo(field.TypeSymbol, out _, out var elem);
            if ((kind == ContainerKind.List || kind == ContainerKind.Dictionary || kind == ContainerKind.HashSet)
                && elem != null
                && ResolveSyncKind(elem) == VersionSyncKind.SyncObject
                && !HasPublicParameterlessConstructor(elem))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.VS0002_SyncTypeMissingPublicParameterlessConstructor,
                    field.Location, elem.ToDisplayString(), field.FieldName));
                return false;
            }
        }

        return true;
    }

    private bool ValidateClass(GeneratorExecutionContext context, VersionFieldClassData classData)
    {
        var classSymbol = classData.ClassSymbol;
        var classDeclaration = classData.ClassDeclaration;
        bool isValid = true;

        // VF10001: Class must be partial
        if (!classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF10001_ClassNotPartial,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name));
            isValid = false;
        }

        // VF10002: Class must implement IVersionElement
        bool implementsInterface = classSymbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == IVersionInterfaceName);

        if (!implementsInterface)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF10002_ClassNotImplementInterface,
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

        // VF20001: Field must have __ prefix
        if (!field.FieldName.StartsWith("__") || field.FieldName.Length <= 2)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF20001_FieldNotPrefixed,
                field.Location,
                field.FieldName));
            isValid = false;
        }

        // VF20002: Field must be private
        if (!field.IsPrivate)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF20002_FieldNotPrivate,
                field.Location,
                field.FieldName));
            isValid = false;
        }

        // VF20003: Check for property name collision (ignore our own generated properties)
        var existingProperty = classData.ClassSymbol.GetMembers(field.PropertyName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !IsAutoGenerated(p));

        if (existingProperty != null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.VF20003_PropertyAlreadyExists,
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

        // IVersion.__Reset: reuse reset — zero version + parent, recurse version-tracked children. A sync class also
        // detaches from its context (clears sync id / dirty). Keeps field values and container contents.
        sb.AppendLine($"{memberIndent}public void __Reset()");
        sb.AppendLine($"{memberIndent}{{");
        foreach (var f in fields)
            if (f.IsVersionType)
                sb.AppendLine($"{memberIndent}    if ({f.FieldName} != null) {f.FieldName}.__Reset();");
        if (syncValid)
        {
            sb.AppendLine($"{memberIndent}    if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);");
            sb.AppendLine($"{memberIndent}    __SyncId = 0; __SyncContext = null; __ClearDirty();");
        }
        sb.AppendLine($"{memberIndent}    __Version = 0; __Parent = null;");
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
            // Synced reference field: assign + wire __Parent (+ container __InitSync), bump the version, then keep
            // the registry in sync — unregister the old subtree, register (assign ids to) the new one so it is
            // ready for the next CaptureFull. When attached, mark this node's reference slot dirty BEFORE attaching
            // the new subtree, so the parent's record (carrying the new child id) is flushed before the children it
            // creates on the consumer (__Recurse(Attach) marks each new node all-dirty).
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
            sb.AppendLine($"{deepIndent}    __MarkDirty({field.SyncSlot});");
            sb.AppendLine($"{deepIndent}    if (value != null) __Recurse(ReactiveBinding.SyncOp.Attach, value);");
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
            // Scalar field (synced or not): assign + version bump. The value is captured by CaptureFull's snapshot;
            // when attached, mark this field's slot dirty so CaptureDelta writes it (the node's id is written once
            // per frame regardless of how many fields changed).
            sb.AppendLine($"{deepIndent}{fieldName} = value;");
            sb.AppendLine($"{deepIndent}__IncrementVersion();");
            if (emitSync)
                sb.AppendLine($"{deepIndent}if (__SyncContext != null) __MarkDirty({field.SyncSlot});");
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

        // Dirty-tracking state. A mutation sets its slot bit (__MarkDirty); CaptureDelta scans the registry by id and
        // writes each node whose masks are non-zero, once. Slots are 0..N-1, chunked into 64-bit masks. Each mask is
        // narrowed on the wire to the smallest type that fits that chunk's field count, so small chunks stay compact.
        int __n = syncFields.Count;
        int __maskCount = (__n + 63) / 64;
        for (int __m = 0; __m < __maskCount; __m++)
        {
            int __chunkSize = MaskChunkSize(__n, __m);
            sb.AppendLine($"{mi}private ulong __dirtyMask{__m};");
            sb.AppendLine($"{mi}private const ulong __fullMask{__m} = {FullMaskLiteral(__chunkSize)};");
        }
        sb.AppendLine();
        sb.AppendLine($"{mi}public bool __IsDirty => {string.Join(" || ", Enumerable.Range(0, __maskCount).Select(i => $"__dirtyMask{i} != 0"))};");
        sb.AppendLine($"{mi}public void __MarkDirty(int slot)");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}switch (slot / 64)");
        sb.AppendLine($"{bi}{{");
        for (int __m = 0; __m < __maskCount; __m++)
            sb.AppendLine($"{ci}case {__m}: __dirtyMask{__m} |= 1UL << (slot & 63); break;");
        sb.AppendLine($"{bi}}}");
        sb.AppendLine($"{mi}}}");
        sb.AppendLine($"{mi}public void __MarkAllDirty() {{ {string.Join(" ", Enumerable.Range(0, __maskCount).Select(i => $"__dirtyMask{i} = __fullMask{i};"))} }}");
        sb.AppendLine($"{mi}public void __ClearDirty() {{ {string.Join(" ", Enumerable.Range(0, __maskCount).Select(i => $"__dirtyMask{i} = 0;"))} }}");
        sb.AppendLine();

        // One node record: [id][mask0][mask1...][changed-field payloads, ascending slot]. __CaptureFull uses the full
        // masks (keyframe), __CaptureDelta uses the dirty masks (incremental); both share __WriteRecord and __Apply.
        string __recordMaskParams = string.Join(", ", Enumerable.Range(0, __maskCount).Select(i => $"ulong __mask{i}"));
        string __fullMaskArgs = string.Join(", ", Enumerable.Range(0, __maskCount).Select(i => $"__fullMask{i}"));
        string __dirtyMaskArgs = string.Join(", ", Enumerable.Range(0, __maskCount).Select(i => $"__dirtyMask{i}"));
        sb.AppendLine($"{mi}private void __WriteRecord(System.IO.BinaryWriter writer, {__recordMaskParams})");
        sb.AppendLine($"{mi}{{");
        sb.AppendLine($"{bi}writer.Write(__SyncId);");
        for (int __m = 0; __m < __maskCount; __m++)
        {
            int __chunkSize = MaskChunkSize(__n, __m);
            sb.AppendLine($"{bi}writer.Write(({MaskTypeName(__chunkSize)})__mask{__m});");
        }
        foreach (var f in syncFields)
        {
            int __m = f.SyncSlot / 64;
            int __bit = f.SyncSlot & 63;
            sb.AppendLine($"{bi}if ((__mask{__m} & (1UL << {__bit})) != 0)");
            sb.AppendLine($"{bi}{{");
            EmitFieldPayloadWrite(sb, bi + "    ", f);
            sb.AppendLine($"{bi}}}");
        }
        sb.AppendLine($"{mi}}}");
        sb.AppendLine();
        sb.AppendLine($"{mi}public void __CaptureFull(System.IO.BinaryWriter writer) {{ __WriteRecord(writer, {__fullMaskArgs}); }}");
        sb.AppendLine();
        sb.AppendLine($"{mi}public void __CaptureDelta(System.IO.BinaryWriter writer) {{ __WriteRecord(writer, {__dirtyMaskArgs}); }}");
        sb.AppendLine();

        // Apply one node record: read every mask chunk, then each set field's payload (ascending slot). The leading
        // [id] header is already consumed by SyncContext.Apply.
        sb.AppendLine($"{mi}public void __Apply(System.IO.BinaryReader reader)");
        sb.AppendLine($"{mi}{{");
        for (int __m = 0; __m < __maskCount; __m++)
        {
            int __chunkSize = MaskChunkSize(__n, __m);
            sb.AppendLine($"{bi}ulong __mask{__m} = reader.{MaskReadMethod(__chunkSize)}();");
        }
        foreach (var f in syncFields)
        {
            int __m = f.SyncSlot / 64;
            int __bit = f.SyncSlot & 63;
            sb.AppendLine($"{bi}if ((__mask{__m} & (1UL << {__bit})) != 0)");
            sb.AppendLine($"{bi}{{");
            EmitFieldReadMasked(sb, bi + "    ", f);
            sb.AppendLine($"{bi}}}");
        }
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
                if (ResolveSyncKind(elem) == VersionSyncKind.SyncObject)
                    EmitSyncFactory(sb, mi, f.PropertyName, elem);
                else
                    EmitScalarDelegate(sb, mi, bi, $"__wVal_{f.PropertyName}", $"__rVal_{f.PropertyName}", elem);
            }
            else if ((kind == ContainerKind.List || kind == ContainerKind.HashSet) && ResolveSyncKind(elem) == VersionSyncKind.SyncObject)
            {
                EmitSyncFactory(sb, mi, f.PropertyName, elem);
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
        sb.AppendLine($"{ci}        child.__MarkAllDirty();");   // newly attached -> flush in full (child before its descendants)
        sb.AppendLine($"{ci}        child.__SyncChildren(ReactiveBinding.SyncOp.Attach);");
        sb.AppendLine($"{ci}    }}");
        sb.AppendLine($"{ci}    break;");
        sb.AppendLine($"{ci}case ReactiveBinding.SyncOp.Unregister:");
        sb.AppendLine($"{ci}    child.__Reset();");   // leaving the graph -> full reset (id/context/dirty/version/parent, recurses)
        sb.AppendLine($"{ci}    break;");
        sb.AppendLine($"{bi}}}");
        sb.AppendLine($"{mi}}}");
    }

    /// <summary>
    /// Emits the read statements for one field inside its <c>if ((mask &amp; bit) != 0) { … }</c> block (the caller's
    /// braces scope the locals). Scalars assign directly; reference fields resolve / create the node inline.
    /// </summary>
    private void EmitFieldReadMasked(StringBuilder sb, string ci, VersionFieldData f)
    {
        if (!IsObjLike(f))
        {
            sb.AppendLine($"{ci}{f.FieldName} = {ScalarReadExpr("reader", f.TypeSymbol)};");
            return;
        }

        var tn = f.TypeSymbol.ToDisplayString();
        sb.AppendLine($"{ci}var __old = {f.FieldName};");
        sb.AppendLine($"{ci}int __id = reader.ReadInt32();");
        sb.AppendLine($"{ci}if (__id == 0) {f.FieldName} = null;");
        sb.AppendLine($"{ci}else if (__SyncContext.__Objects.TryGetValue(__id, out var __n)) {f.FieldName} = ({tn})__n;");
        sb.AppendLine($"{ci}else {{ var __c = new {tn}(); __c.__SyncId = __id; __c.__SyncContext = __SyncContext; __SyncContext.__Objects[__id] = __c; {f.FieldName} = __c; }}");
        sb.AppendLine($"{ci}if (__old != null && !object.ReferenceEquals(__old, {f.FieldName})) __old.__Parent = null;");
        if (f.SyncKind == VersionSyncKind.Container)
            sb.AppendLine($"{ci}if ({f.FieldName} != null) {{ {f.FieldName}.__Parent = this; {f.FieldName}.__InitSync({ContainerInitArgs(f)}); }}");
        else
            sb.AppendLine($"{ci}if ({f.FieldName} != null) {f.FieldName}.__Parent = this;");
    }

    // ----- Dirty-mask wire helpers: the mask is narrowed to the smallest type that fits N slots. -----

    /// <summary>The unsigned type used to (de)serialize an N-slot dirty mask.</summary>
    private static string MaskTypeName(int n)
        => n <= 8 ? "byte" : n <= 16 ? "ushort" : n <= 32 ? "uint" : "ulong";

    /// <summary>The BinaryReader method that reads an N-slot mask (widened into a ulong by the caller).</summary>
    private static string MaskReadMethod(int n)
        => n <= 8 ? "ReadByte" : n <= 16 ? "ReadUInt16" : n <= 32 ? "ReadUInt32" : "ReadUInt64";

    /// <summary>The number of field slots in a 64-field mask chunk.</summary>
    private static int MaskChunkSize(int fieldCount, int maskIndex)
    {
        int remaining = fieldCount - maskIndex * 64;
        return remaining >= 64 ? 64 : remaining;
    }

    /// <summary>The all-slots mask literal for one chunk (N is 1..64). Avoids 1UL&lt;&lt;64 UB.</summary>
    private static string FullMaskLiteral(int n)
    {
        ulong full = n >= 64 ? ulong.MaxValue : (n <= 0 ? 0UL : (1UL << n) - 1UL);
        return "0x" + full.ToString("X") + "UL";
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

    private static void EmitSyncFactory(StringBuilder sb, string mi, string propertyName, ITypeSymbol t)
    {
        var tn = t.ToDisplayString();
        sb.AppendLine();
        sb.AppendLine($"{mi}private static ReactiveBinding.IVersionSync __new_{propertyName}() => new {tn}();");
    }

    private static bool IsObjLike(VersionFieldData f)
        => f.SyncKind == VersionSyncKind.SyncObject || f.SyncKind == VersionSyncKind.Container;

    private static string ContainerInitArgs(VersionFieldData f)
    {
        var kind = GetContainerInfo(f.TypeSymbol, out _, out var elem);
        if (kind == ContainerKind.Dictionary)
        {
            if (elem != null && ResolveSyncKind(elem) == VersionSyncKind.SyncObject)
                return $"__wKey_{f.PropertyName}, __rKey_{f.PropertyName}, __new_{f.PropertyName}";
            return $"__wKey_{f.PropertyName}, __rKey_{f.PropertyName}, __wVal_{f.PropertyName}, __rVal_{f.PropertyName}";
        }
        if ((kind == ContainerKind.List || kind == ContainerKind.HashSet) && elem != null && ResolveSyncKind(elem) == VersionSyncKind.SyncObject)
            return $"__new_{f.PropertyName}";   // object elements/values: inject factory
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
        // Only the sync container variants are syncable. A base (version-only) VersionList/etc. as a synced field
        // is not a Container -> ResolveSyncKind falls to None -> VS0001.
        if (def == "ReactiveBinding.VersionSyncList<T>") { value = named.TypeArguments[0]; return ContainerKind.List; }
        if (def == "ReactiveBinding.VersionSyncHashSet<T>") { value = named.TypeArguments[0]; return ContainerKind.HashSet; }
        if (def == "ReactiveBinding.VersionSyncDictionary<TKey, TValue>") { key = named.TypeArguments[0]; value = named.TypeArguments[1]; return ContainerKind.Dictionary; }
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

    // A syncable container is one of the sync variants. The version-only base containers (VersionList/etc.) are
    // not syncable, so using one as a synced [VersionField] resolves to None and is reported VS0001.
    private static bool IsVersionContainer(ITypeSymbol t)
    {
        if (t is not INamedTypeSymbol named || !named.IsGenericType) return false;
        var def = named.ConstructedFrom.ToDisplayString();
        return def == "ReactiveBinding.VersionSyncList<T>"
            || def == "ReactiveBinding.VersionSyncDictionary<TKey, TValue>"
            || def == "ReactiveBinding.VersionSyncHashSet<T>";
    }

    private static bool ImplementsIVersionSync(ITypeSymbol t)
        => t.AllInterfaces.Any(i => i.ToDisplayString() == "ReactiveBinding.IVersionSync");

    private static bool HasPublicParameterlessConstructor(ITypeSymbol t)
        => t is INamedTypeSymbol named
        && named.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

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
