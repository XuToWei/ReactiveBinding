using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveBinding.Generator;

internal class VersionFieldClassData
{
    public INamedTypeSymbol ClassSymbol { get; set; } = null!;
    public ClassDeclarationSyntax ClassDeclaration { get; set; } = null!;
    public List<VersionFieldData> Fields { get; } = new();
}

internal class VersionFieldData
{
    public string FieldName { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public ITypeSymbol TypeSymbol { get; set; } = null!;
    public Location Location { get; set; } = Location.None;
    public bool IsPrivate { get; set; }
    public bool IsVersionType { get; set; }
}

internal class VersionFieldSyntaxReceiver : ISyntaxContextReceiver
{
    private const string VersionFieldAttributeName = "ReactiveBinding.VersionFieldAttribute";
    private const string IVersionInterfaceName = "ReactiveBinding.IVersion";

    public List<VersionFieldClassData> ClassDataList { get; } = new();

    private readonly Dictionary<INamedTypeSymbol, VersionFieldClassData> _classDataMap
        = new(SymbolEqualityComparer.Default);

    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        if (context.Node is FieldDeclarationSyntax fieldDeclaration)
        {
            ProcessFieldDeclaration(context, fieldDeclaration);
        }
    }

    private void ProcessFieldDeclaration(GeneratorSyntaxContext context,
        FieldDeclarationSyntax fieldDeclaration)
    {
        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            if (context.SemanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                continue;

            var versionAttr = fieldSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == VersionFieldAttributeName);

            if (versionAttr == null)
                continue;

            var classSymbol = fieldSymbol.ContainingType;
            var classDeclaration = GetClassDeclaration(fieldDeclaration);
            if (classDeclaration == null) continue;

            var classData = GetOrCreateClassData(classSymbol, classDeclaration);

            string fieldName = fieldSymbol.Name;
            string propertyName = ConvertToPropertyName(fieldName);

            // Check if field type implements IVersion
            bool isVersionType = fieldSymbol.Type.AllInterfaces.Any(i =>
                i.ToDisplayString() == IVersionInterfaceName);

            classData.Fields.Add(new VersionFieldData
            {
                FieldName = fieldName,
                PropertyName = propertyName,
                TypeSymbol = fieldSymbol.Type,
                Location = variable.Identifier.GetLocation(),
                IsPrivate = fieldSymbol.DeclaredAccessibility == Accessibility.Private,
                IsVersionType = isVersionType
            });
        }
    }

    private static string ConvertToPropertyName(string fieldName)
    {
        // Remove m_ prefix and capitalize first letter
        // m_Health -> Health, m_playerName -> PlayerName
        if (fieldName.StartsWith("m_") && fieldName.Length > 2)
        {
            return char.ToUpper(fieldName[2]) + fieldName.Substring(3);
        }
        return fieldName;
    }

    private VersionFieldClassData GetOrCreateClassData(INamedTypeSymbol classSymbol,
        ClassDeclarationSyntax classDeclaration)
    {
        if (!_classDataMap.TryGetValue(classSymbol, out var classData))
        {
            classData = new VersionFieldClassData
            {
                ClassSymbol = classSymbol,
                ClassDeclaration = classDeclaration
            };
            _classDataMap[classSymbol] = classData;
            ClassDataList.Add(classData);
        }
        return classData;
    }

    private static ClassDeclarationSyntax? GetClassDeclaration(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is ClassDeclarationSyntax classDeclaration)
                return classDeclaration;
            parent = parent.Parent;
        }
        return null;
    }
}
