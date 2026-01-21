using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace ReactiveBinding.Generator;

internal class ReactiveClassData
{
    public INamedTypeSymbol ClassSymbol { get; set; } = null!;
    public ClassDeclarationSyntax ClassDeclaration { get; set; } = null!;
    public int? ThrottleCallCount { get; set; }
    public List<ReactiveSourceData> Sources { get; } = new();
    public List<ReactiveBindData> Bindings { get; } = new();
}

internal class ReactiveSourceData
{
    public string MemberName { get; set; } = "";
    public ReactiveSourceKind MemberKind { get; set; }
    public ITypeSymbol TypeSymbol { get; set; } = null!;
    public Location Location { get; set; } = Location.None;
    public bool HasGetter { get; set; }
    public bool HasParameters { get; set; }
    public bool IsVersionContainer { get; set; }
}

internal enum ReactiveSourceKind
{
    Field,
    Property,
    Method
}

internal class ReactiveBindData
{
    public string MethodName { get; set; } = "";
    public string[] ReactiveIds { get; set; } = System.Array.Empty<string>();
    public ITypeSymbol[] ParameterTypes { get; set; } = System.Array.Empty<ITypeSymbol>();
    public Location Location { get; set; } = Location.None;
    public bool IsStatic { get; set; }
    public bool ReturnsVoid { get; set; }
    public bool UsesNameof { get; set; }
    public bool IsAutoInferred { get; set; }
    public MethodDeclarationSyntax? MethodSyntax { get; set; }
}
