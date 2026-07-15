using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveBinding.Generator;

/// <summary>
/// Binds attributes written in a <c>[VersionProperty: ...]</c> target list and renders them for
/// the generated property. The C# compiler deliberately omits an unrecognized target list from
/// <see cref="IFieldSymbol.GetAttributes"/>, so this path works from syntax plus the semantic model.
/// </summary>
internal static class VersionPropertyAttributeFormatter
{
    public const string TargetName = "VersionProperty";

    private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static bool IsVersionPropertyTarget(AttributeListSyntax list)
        => string.Equals(list.Target?.Identifier.ValueText, TargetName, StringComparison.Ordinal);

    public static bool TryFormat(
        SemanticModel semanticModel,
        AttributeSyntax attribute,
        out string text,
        out string error)
    {
        text = "";
        error = "";

        var symbolInfo = semanticModel.GetSymbolInfo(attribute);
        if (symbolInfo.Symbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            error = "the attribute type or constructor could not be resolved";
            return false;
        }

        var attributeType = constructor.ContainingType;
        if (!IsAttributeType(attributeType, semanticModel.Compilation))
        {
            error = $"'{attributeType.ToDisplayString()}' is not an attribute type";
            return false;
        }

        if (!AllowsPropertyTarget(attributeType, semanticModel.Compilation))
        {
            error = $"'{attributeType.ToDisplayString()}' cannot be applied to properties";
            return false;
        }

        var builder = new StringBuilder();
        builder.Append(attributeType.ToDisplayString(FullyQualifiedTypeFormat));

        if (attribute.ArgumentList != null)
        {
            var arguments = new List<string>(attribute.ArgumentList.Arguments.Count);
            foreach (var argument in attribute.ArgumentList.Arguments)
            {
                if (!TryFormatExpression(
                        semanticModel,
                        argument.Expression,
                        out var expression,
                        out error))
                {
                    return false;
                }

                if (argument.NameEquals != null)
                    expression = $"{argument.NameEquals.Name} = {expression}";
                else if (argument.NameColon != null)
                    expression = $"{argument.NameColon.Name}: {expression}";

                arguments.Add(expression);
            }

            builder.Append('(');
            builder.Append(string.Join(", ", arguments));
            builder.Append(')');
        }

        text = builder.ToString();
        return true;
    }

    private static bool TryFormatExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        out string text,
        out string error)
    {
        text = "";
        error = "";

        var typeInfo = semanticModel.GetTypeInfo(expression);
        bool boxesToObject = typeInfo.ConvertedType?.SpecialType == SpecialType.System_Object;

        if (expression is CastExpressionSyntax castExpression)
        {
            var castType = semanticModel.GetTypeInfo(castExpression.Type).Type;
            if (castType == null || castType.TypeKind == TypeKind.Error)
            {
                error = $"the cast type in '{expression}' could not be resolved";
                return false;
            }

            if (!TryFormatExpression(
                    semanticModel,
                    castExpression.Expression,
                    out var operand,
                    out error))
            {
                return false;
            }

            text = $"({castType.ToDisplayString(FullyQualifiedTypeFormat)})({operand})";
            return true;
        }

        if (expression is TypeOfExpressionSyntax typeOfExpression)
        {
            var typeOfType = semanticModel.GetTypeInfo(typeOfExpression.Type).Type;
            if (typeOfType == null || typeOfType.TypeKind == TypeKind.Error)
            {
                error = $"the type in '{expression}' could not be resolved";
                return false;
            }

            text = BoxIfNeeded(
                $"typeof({typeOfType.ToDisplayString(FullyQualifiedTypeFormat)})",
                boxesToObject);
            return true;
        }

        if (expression is ArrayCreationExpressionSyntax arrayCreation)
        {
            if (!TryFormatArray(
                semanticModel,
                expression,
                arrayCreation.Initializer,
                out text,
                out error))
            {
                return false;
            }

            text = BoxIfNeeded(text, boxesToObject);
            return true;
        }

        if (expression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
        {
            if (!TryFormatArray(
                semanticModel,
                expression,
                implicitArrayCreation.Initializer,
                out text,
                out error))
            {
                return false;
            }

            text = BoxIfNeeded(text, boxesToObject);
            return true;
        }

        var constant = semanticModel.GetConstantValue(expression);
        if (!constant.HasValue)
        {
            error = $"argument '{expression}' is not a supported compile-time attribute value";
            return false;
        }

        var type = boxesToObject
            ? typeInfo.Type
            : typeInfo.ConvertedType ?? typeInfo.Type;

        if (!TryFormatConstant(constant.Value, type, out text, out error))
            return false;

        text = BoxIfNeeded(text, boxesToObject);
        return true;
    }

    private static bool TryFormatArray(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        InitializerExpressionSyntax? initializer,
        out string text,
        out string error)
    {
        text = "";
        error = "";

        var typeInfo = semanticModel.GetTypeInfo(expression);
        var arrayType = typeInfo.ConvertedType as IArrayTypeSymbol
            ?? typeInfo.Type as IArrayTypeSymbol;
        if (arrayType == null || arrayType.Rank != 1 || initializer == null)
        {
            error = $"array argument '{expression}' must be a one-dimensional initialized array";
            return false;
        }

        var values = new List<string>(initializer.Expressions.Count);
        foreach (var item in initializer.Expressions)
        {
            if (!TryFormatExpression(semanticModel, item, out var value, out error))
                return false;
            values.Add(value);
        }

        text = $"new {arrayType.ElementType.ToDisplayString(FullyQualifiedTypeFormat)}[] {{ {string.Join(", ", values)} }}";
        return true;
    }

    private static bool TryFormatConstant(
        object? value,
        ITypeSymbol? type,
        out string text,
        out string error)
    {
        text = "";
        error = "";

        if (value == null)
        {
            text = type != null && type.TypeKind != TypeKind.Error
                ? $"({type.ToDisplayString(FullyQualifiedTypeFormat)})null"
                : "null";
            return true;
        }

        if (type?.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            var underlyingType = enumType.EnumUnderlyingType;
            if (underlyingType == null || !TryFormatNumeric(value, underlyingType.SpecialType, out var number))
            {
                error = $"enum value '{value}' could not be formatted";
                return false;
            }

            text = $"({enumType.ToDisplayString(FullyQualifiedTypeFormat)}){number}";
            return true;
        }

        var specialType = type?.SpecialType ?? SpecialType.None;
        if (specialType == SpecialType.System_Object)
            specialType = GetRuntimeSpecialType(value);

        switch (specialType)
        {
            case SpecialType.System_Boolean:
                text = (bool)value ? "true" : "false";
                return true;
            case SpecialType.System_Char:
                text = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral((char)value, quote: true);
                return true;
            case SpecialType.System_String:
                text = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral((string)value, quote: true);
                return true;
            case SpecialType.System_Single:
                text = FormatSingle(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                return true;
            case SpecialType.System_Double:
                text = FormatDouble(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                return true;
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                if (TryFormatNumeric(value, specialType, out text))
                    return true;
                break;
        }

        error = $"attribute value '{value}' of type '{type?.ToDisplayString() ?? value.GetType().Name}' is not supported";
        return false;
    }

    private static bool TryFormatNumeric(object value, SpecialType type, out string text)
    {
        text = type switch
        {
            SpecialType.System_SByte => $"(sbyte){Convert.ToSByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            SpecialType.System_Byte => $"(byte){Convert.ToByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            SpecialType.System_Int16 => $"(short){Convert.ToInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            SpecialType.System_UInt16 => $"(ushort){Convert.ToUInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            SpecialType.System_Int32 => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            SpecialType.System_UInt32 => Convert.ToUInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "U",
            SpecialType.System_Int64 => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L",
            SpecialType.System_UInt64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "UL",
            _ => ""
        };
        return text.Length > 0;
    }

    private static string FormatSingle(float value)
    {
        if (float.IsNaN(value)) return "float.NaN";
        if (float.IsPositiveInfinity(value)) return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(value)) return "float.NegativeInfinity";
        return value.ToString("R", CultureInfo.InvariantCulture) + "F";
    }

    private static string FormatDouble(double value)
    {
        if (double.IsNaN(value)) return "double.NaN";
        if (double.IsPositiveInfinity(value)) return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(value)) return "double.NegativeInfinity";
        return value.ToString("R", CultureInfo.InvariantCulture) + "D";
    }

    private static SpecialType GetRuntimeSpecialType(object value)
    {
        return value switch
        {
            bool => SpecialType.System_Boolean,
            char => SpecialType.System_Char,
            string => SpecialType.System_String,
            sbyte => SpecialType.System_SByte,
            byte => SpecialType.System_Byte,
            short => SpecialType.System_Int16,
            ushort => SpecialType.System_UInt16,
            int => SpecialType.System_Int32,
            uint => SpecialType.System_UInt32,
            long => SpecialType.System_Int64,
            ulong => SpecialType.System_UInt64,
            float => SpecialType.System_Single,
            double => SpecialType.System_Double,
            _ => SpecialType.None
        };
    }

    private static string BoxIfNeeded(string value, bool boxesToObject)
        => boxesToObject ? $"(object)({value})" : value;

    private static bool IsAttributeType(INamedTypeSymbol type, Compilation compilation)
    {
        var attributeBase = compilation.GetTypeByMetadataName("System.Attribute");
        if (attributeBase == null)
            return false;

        for (var current = type; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, attributeBase))
                return true;
        }
        return false;
    }

    private static bool AllowsPropertyTarget(INamedTypeSymbol type, Compilation compilation)
    {
        var usageType = compilation.GetTypeByMetadataName("System.AttributeUsageAttribute");
        if (usageType == null)
            return true;

        for (var current = type; current != null; current = current.BaseType)
        {
            var usage = current.GetAttributes().FirstOrDefault(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, usageType));
            if (usage == null)
                continue;

            if (usage.ConstructorArguments.Length == 0 || usage.ConstructorArguments[0].Value == null)
                return true;

            var targets = (AttributeTargets)Convert.ToInt32(
                usage.ConstructorArguments[0].Value,
                CultureInfo.InvariantCulture);
            return (targets & AttributeTargets.Property) != 0;
        }

        return true;
    }
}
