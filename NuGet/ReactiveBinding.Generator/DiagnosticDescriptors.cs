using Microsoft.CodeAnalysis;

namespace ReactiveBinding.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "ReactiveBinding";

    // RB0xxx
    public static readonly DiagnosticDescriptor RB0001_UnmatchedSource = new(
        id: "RB0001",
        title: "Unmatched ReactiveSource",
        messageFormat: "ReactiveSource '{0}' has no corresponding ReactiveBind",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB0003_ObserveChangesNotCalled = new(
        id: "RB0003",
        title: "ObserveChanges not called",
        messageFormat: "Class '{0}' implements IReactiveObserver but does not call ObserveChanges(). Please call ObserveChanges() in your code, or add [ReactiveObserveIgnore] if it is called externally.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB0002_UnmatchedBind = new(
        id: "RB0002",
        title: "Unmatched ReactiveBind",
        messageFormat: "ReactiveBind on method '{0}' references non-existent sources: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Class-level errors (RB1xxx)
    public static readonly DiagnosticDescriptor RB10001_ClassNotPartial = new(
        id: "RB10001",
        title: "Class not partial",
        messageFormat: "Class '{0}' with ReactiveBind must be declared as partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB10002_ClassNotImplementInterface = new(
        id: "RB10002",
        title: "Class not implementing IReactiveObserver",
        messageFormat: "Class '{0}' with ReactiveBind must implement IReactiveObserver interface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB10003_ThrottleInvalidValue = new(
        id: "RB10003",
        title: "Invalid ReactiveThrottle value",
        messageFormat: "ReactiveThrottle value must be >= 1, got {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB10004_ThrottleWithoutInterface = new(
        id: "RB10004",
        title: "ReactiveThrottle without IReactiveObserver",
        messageFormat: "Class '{0}' has ReactiveThrottle but does not implement IReactiveObserver",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ReactiveSource errors (RB2xxx)
    public static readonly DiagnosticDescriptor RB20001_MethodReturnsVoid = new(
        id: "RB20001",
        title: "ReactiveSource method returns void",
        messageFormat: "ReactiveSource method '{0}' must have a return type (cannot be void)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB20002_PropertyNoGetter = new(
        id: "RB20002",
        title: "ReactiveSource property has no getter",
        messageFormat: "ReactiveSource property '{0}' must have a getter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB20003_MethodHasParameters = new(
        id: "RB20003",
        title: "ReactiveSource method has parameters",
        messageFormat: "ReactiveSource method '{0}' must have no parameters",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB20004_UnsupportedSourceType = new(
        id: "RB20004",
        title: "Unsupported ReactiveSource type",
        messageFormat: "ReactiveSource '{0}' has unsupported type '{1}'. Only primitive types, structs, and IVersion types are supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB20005_StructMissingEqualityOperator = new(
        id: "RB20005",
        title: "Struct missing equality operator",
        messageFormat: "ReactiveSource '{0}' uses struct type '{1}' which must implement == and != operators",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ReactiveBind errors (RB3xxx)
    public static readonly DiagnosticDescriptor RB30001_BindEmptyIds = new(
        id: "RB30001",
        title: "ReactiveBind has no identities",
        messageFormat: "ReactiveBind on method '{0}' must specify at least one source identity",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB30002_MethodIsStatic = new(
        id: "RB30002",
        title: "ReactiveBind method is static",
        messageFormat: "ReactiveBind method '{0}' cannot be static",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB30003_MethodNotVoid = new(
        id: "RB30003",
        title: "ReactiveBind method does not return void",
        messageFormat: "ReactiveBind method '{0}' must return void",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB30004_InvalidParameterCount = new(
        id: "RB30004",
        title: "Invalid parameter count",
        messageFormat: "ReactiveBind method '{0}' binds {1} sources but has {2} parameters. Valid signatures: {3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB30005_ParameterTypeMismatch = new(
        id: "RB30005",
        title: "Parameter type mismatch",
        messageFormat: "ReactiveBind method '{0}' parameter {1} has type '{2}' but source has type '{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB30006_DuplicateIds = new(
        id: "RB30006",
        title: "Duplicate identities in ReactiveBind",
        messageFormat: "ReactiveBind on method '{0}' has duplicate identities: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB30007_NotUsingNameof = new(
        id: "RB30007",
        title: "ReactiveBind not using nameof()",
        messageFormat: "ReactiveBind on method '{0}' must use nameof() expressions for source identities",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB30008_NoSourcesInferred = new(
        id: "RB30008",
        title: "No ReactiveSource found in method body",
        messageFormat: "ReactiveBind on method '{0}' uses auto-inference but no ReactiveSource members are referenced in the method body",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB30009_AutoInferredWithParameters = new(
        id: "RB30009",
        title: "Auto-inferred ReactiveBind cannot have parameters",
        messageFormat: "ReactiveBind on method '{0}' uses auto-inference and must have no parameters",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB30010_SourceNotMarked = new(
        id: "RB30010",
        title: "Referenced member not marked with ReactiveSource",
        messageFormat: "ReactiveBind on method '{0}' references '{1}' which exists but is not marked with [ReactiveSource]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB10005_ManualObserveChanges = new(
        id: "RB10005",
        title: "Manual ObserveChanges implementation",
        messageFormat: "Method '{0}' in class '{1}' is auto-generated by the source generator and must not be manually implemented",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB10006_ManualResetChanges = new(
        id: "RB10006",
        title: "Manual ResetChanges implementation",
        messageFormat: "Method '{0}' in class '{1}' is auto-generated by the source generator and must not be manually implemented",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // VersionField class-level errors (VF1xxx)
    public static readonly DiagnosticDescriptor VF10001_ClassNotPartial = new(
        id: "VF10001",
        title: "Class not partial",
        messageFormat: "Class '{0}' with VersionField must be declared as partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor VF10002_ClassNotImplementInterface = new(
        id: "VF10002",
        title: "Class not implementing IVersion",
        messageFormat: "Class '{0}' with VersionField must implement IVersion interface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // VersionField field-level errors (VF2xxx)
    public static readonly DiagnosticDescriptor VF20001_FieldNotPrefixed = new(
        id: "VF20001",
        title: "Field missing __ prefix",
        messageFormat: "VersionField '{0}' must have '__' prefix",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor VF20002_FieldNotPrivate = new(
        id: "VF20002",
        title: "Field not private",
        messageFormat: "VersionField '{0}' must be private",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor VF20003_PropertyAlreadyExists = new(
        id: "VF20003",
        title: "Property already exists",
        messageFormat: "Property '{0}' already exists, cannot generate from field '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // VersionField usage errors (VF3xxx)
    public static readonly DiagnosticDescriptor VF30001_ParentAccessNotAllowed = new(
        id: "VF30001",
        title: "__Parent property access not allowed",
        messageFormat: "IVersion.__Parent can only be accessed within IVersion implementations. It is managed internally by containers and generated code.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor VF30002_DirectFieldAccess = new(
        id: "VF30002",
        title: "Direct access to VersionField backing field",
        messageFormat: "Field '{0}' is marked with [VersionField] and should only be accessed through the generated property '{1}'. Direct access bypasses version tracking.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor VF30003_FieldHasInitializer = new(
        id: "VF30003",
        title: "VersionField must not have a default value initializer",
        messageFormat: "VersionField '{0}' must not have a default value initializer. Assign through the generated property '{1}' instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // VSxxxx (data synchronization)
    public static readonly DiagnosticDescriptor VS0001_UnsupportedSyncType = new(
        id: "VS0001",
        title: "Unsupported synced field type",
        messageFormat: "[VersionField] '{0}' in an IVersionSync class has type '{1}' which cannot be synchronized. Use a primitive/enum/string, a nested concrete IVersionSync type, or a Version container.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor VS0002_TooManySyncFields = new(
        id: "VS0002",
        title: "Too many synced fields",
        messageFormat: "IVersionSync class '{0}' has {1} synced [VersionField]s; synchronization supports at most 64 (the per-node change mask is 64-bit).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor VS0003_SyncTypeMissingPublicParameterlessConstructor = new(
        id: "VS0003",
        title: "Synced object type missing public parameterless constructor",
        messageFormat: "Synced object type '{0}' used by [VersionField] '{1}' must have a public parameterless constructor so synchronization can create it when applying data.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
