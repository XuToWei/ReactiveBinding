using Microsoft.CodeAnalysis;

namespace ReactiveBinding.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "ReactiveBinding";

    // Warnings (RB0xxx)
    public static readonly DiagnosticDescriptor RB0001_UnmatchedSource = new(
        id: "RB0001",
        title: "Unmatched ReactiveSource",
        messageFormat: "ReactiveSource '{0}' has no corresponding ReactiveBind",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB0002_UnmatchedBind = new(
        id: "RB0002",
        title: "Unmatched ReactiveBind",
        messageFormat: "ReactiveBind on method '{0}' references non-existent sources: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Class-level errors (RB1xxx)
    public static readonly DiagnosticDescriptor RB1001_ClassNotPartial = new(
        id: "RB1001",
        title: "Class not partial",
        messageFormat: "Class '{0}' with ReactiveBind must be declared as partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB1002_ClassNotImplementInterface = new(
        id: "RB1002",
        title: "Class not implementing IReactiveObserver",
        messageFormat: "Class '{0}' with ReactiveBind must implement IReactiveObserver interface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB1003_ThrottleInvalidValue = new(
        id: "RB1003",
        title: "Invalid ReactiveThrottle value",
        messageFormat: "ReactiveThrottle value must be >= 1, got {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB1004_ThrottleWithoutInterface = new(
        id: "RB1004",
        title: "ReactiveThrottle without IReactiveObserver",
        messageFormat: "Class '{0}' has ReactiveThrottle but does not implement IReactiveObserver",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ReactiveSource errors (RB2xxx)
    public static readonly DiagnosticDescriptor RB2001_MethodReturnsVoid = new(
        id: "RB2001",
        title: "ReactiveSource method returns void",
        messageFormat: "ReactiveSource method '{0}' must have a return type (cannot be void)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB2002_PropertyNoGetter = new(
        id: "RB2002",
        title: "ReactiveSource property has no getter",
        messageFormat: "ReactiveSource property '{0}' must have a getter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB2003_MethodHasParameters = new(
        id: "RB2003",
        title: "ReactiveSource method has parameters",
        messageFormat: "ReactiveSource method '{0}' must have no parameters",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ReactiveBind errors (RB3xxx)
    public static readonly DiagnosticDescriptor RB3001_BindEmptyIds = new(
        id: "RB3001",
        title: "ReactiveBind has no identities",
        messageFormat: "ReactiveBind on method '{0}' must specify at least one source identity",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB3002_MethodIsStatic = new(
        id: "RB3002",
        title: "ReactiveBind method is static",
        messageFormat: "ReactiveBind method '{0}' cannot be static",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB3003_MethodNotVoid = new(
        id: "RB3003",
        title: "ReactiveBind method does not return void",
        messageFormat: "ReactiveBind method '{0}' must return void",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB3004_InvalidParameterCount = new(
        id: "RB3004",
        title: "Invalid parameter count",
        messageFormat: "ReactiveBind method '{0}' binds {1} sources but has {2} parameters. Valid signatures: {3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB3005_ParameterTypeMismatch = new(
        id: "RB3005",
        title: "Parameter type mismatch",
        messageFormat: "ReactiveBind method '{0}' parameter {1} has type '{2}' but source has type '{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB3006_DuplicateIds = new(
        id: "RB3006",
        title: "Duplicate identities in ReactiveBind",
        messageFormat: "ReactiveBind on method '{0}' has duplicate identities: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RB3007_NotUsingNameof = new(
        id: "RB3007",
        title: "ReactiveBind not using nameof()",
        messageFormat: "ReactiveBind on method '{0}' must use nameof() expressions for source identities",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
