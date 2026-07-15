using System.Linq;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class VersionPropertyTargetTests
{
    [Test]
    public void VersionPropertyTarget_ConstructorAndNamedArguments_AreGeneratedOnProperty()
    {
        var source = @"
using System;
namespace Test
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class LabelAttribute : Attribute
    {
        public LabelAttribute(string name, int order) { }
        public bool Enabled { get; set; }
    }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionProperty: Label(""health"", 2, Enabled = true)]
        private int __Health;
    }
}";

        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(
            result,
            "[global::Test.LabelAttribute(\"health\", 2, Enabled = true)]");
    }

    [Test]
    public void VersionPropertyTarget_MultipleAttributes_AreGeneratedInSourceOrder()
    {
        var source = @"
using System;
namespace Test
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class FirstAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SecondAttribute : Attribute
    {
        public SecondAttribute(string value) { }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ThirdAttribute : Attribute { }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionProperty: First, Second(""value"")]
        [VersionProperty: Third]
        private int __Health;
    }
}";

        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        var generated = GeneratorTestHelper.GetGeneratedForClass(result, "TestClass");

        GeneratorTestHelper.AssertNoErrors(result);
        var first = generated.IndexOf("[global::Test.FirstAttribute]", System.StringComparison.Ordinal);
        var second = generated.IndexOf(
            "[global::Test.SecondAttribute(\"value\")]",
            System.StringComparison.Ordinal);
        var third = generated.IndexOf("[global::Test.ThirdAttribute]", System.StringComparison.Ordinal);
        Assert.That(first, Is.GreaterThanOrEqualTo(0));
        Assert.That(second, Is.GreaterThan(first));
        Assert.That(third, Is.GreaterThan(second));
    }

    [Test]
    public void VersionPropertyTarget_AttributesStayAssociatedWithTheirGeneratedProperties()
    {
        var source = @"
using System;
namespace Test
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class TagAttribute : Attribute
    {
        public TagAttribute(string name) { Name = name; }
        public string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class HideInInspector : Attribute { }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionProperty: Tag(""health""), HideInInspector]
        private int __Health;

        [VersionField]
        [VersionProperty: Tag(""mana"")]
        private int __Mana;
    }
}";

        var compiled = GeneratorTestHelper.CompileAndRun(source);
        var type = compiled.Assembly.GetType("Test.TestClass")!;
        var healthAttributes = type.GetProperty("Health")!.GetCustomAttributes(inherit: false);
        var manaAttributes = type.GetProperty("Mana")!.GetCustomAttributes(inherit: false);
        var healthTag = healthAttributes.Single(attribute => attribute.GetType().Name == "TagAttribute");
        var manaTag = manaAttributes.Single(attribute => attribute.GetType().Name == "TagAttribute");

        Assert.That(healthTag.GetType().GetProperty("Name")!.GetValue(healthTag), Is.EqualTo("health"));
        Assert.That(manaTag.GetType().GetProperty("Name")!.GetValue(manaTag), Is.EqualTo("mana"));
        Assert.That(healthAttributes.Any(attribute => attribute.GetType().Name == "HideInInspector"), Is.True);
        Assert.That(manaAttributes.Any(attribute => attribute.GetType().Name == "HideInInspector"), Is.False);
    }

    [Test]
    public void VersionPropertyTarget_FieldOnlyAttribute_ReportsSourceDiagnostic()
    {
        var source = @"
using System;
namespace Test
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FieldOnlyAttribute : Attribute { }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionProperty: FieldOnly]
        private int __Health;
    }
}";

        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "VF10013"), Is.True);
        Assert.That(
            result.CompilationDiagnostics.Any(d => d.Id == "CS0592"),
            Is.False,
            "The generator should report the invalid target at the source instead of emitting invalid generated code.");
    }

    [Test]
    public void VersionPropertyTarget_SemanticArguments_AreRenderedWithoutSourceUsings()
    {
        var source = @"
using System;
using Meta = Test.MetadataAttribute;
using Text = System.String;
namespace Test
{
    public enum Mode { Basic = 1, Advanced = 2 }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class MetadataAttribute : Attribute
    {
        public MetadataAttribute(Mode mode, Type type, int[] values) { }
        public string Name { get; set; }
    }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionProperty: Meta(Mode.Advanced, typeof(Text), new[] { 1, 2 }, Name = nameof(TestClass))]
        private int __Health;
    }
}";

        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(
            result,
            "[global::Test.MetadataAttribute((global::Test.Mode)2, typeof(string), new int[] { 1, 2 }, Name = \"TestClass\")]" );
    }

    [Test]
    public void VersionPropertyTarget_UnresolvedConstructor_ReportsSourceDiagnostic()
    {
        var source = @"
using System;
namespace Test
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class LabelAttribute : Attribute
    {
        public LabelAttribute(string value) { }
    }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionProperty: Label(123)]
        private int __Health;
    }
}";

        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "VF10013"), Is.True);
    }

    [Test]
    public void VersionPropertyTarget_TypedConstants_PreserveConstructorSelection()
    {
        var source = @"
using System;
namespace Test
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ChoiceAttribute : Attribute
    {
        public ChoiceAttribute(string value) { Kind = ""string""; }
        public ChoiceAttribute(Type value) { Kind = ""type""; }
        public string Kind { get; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class BoxAttribute : Attribute
    {
        public BoxAttribute(object value) { Kind = ""object""; }
        public BoxAttribute(string value) { Kind = ""string""; }
        public string Kind { get; }
    }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionProperty: Choice((string)null), Box((object)""value"")]
        private int __Health;
    }
}";

        var compiled = GeneratorTestHelper.CompileAndRun(source);
        var property = compiled.Assembly.GetType("Test.TestClass")!.GetProperty("Health")!;
        var attributes = property.GetCustomAttributes(inherit: false);
        var choice = attributes.Single(attribute => attribute.GetType().Name == "ChoiceAttribute");
        var box = attributes.Single(attribute => attribute.GetType().Name == "BoxAttribute");

        Assert.That(choice.GetType().GetProperty("Kind")!.GetValue(choice), Is.EqualTo("string"));
        Assert.That(box.GetType().GetProperty("Kind")!.GetValue(box), Is.EqualTo("object"));
    }

    [Test]
    public async System.Threading.Tasks.Task VersionPropertyTargetSuppressor_SuppressesCs0658ForVersionField()
    {
        var source = @"
using System;
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionProperty: Obsolete(""old"")]
        private int __Health;
    }
}";

        var diagnostics = await GeneratorTestHelper.RunVersionPropertyTargetSuppressor(source);
        Assert.That(
            diagnostics.Any(d => d.Id == "CS0658" && !d.IsSuppressed),
            Is.False);

        var includingSuppressed = await GeneratorTestHelper.RunVersionPropertyTargetSuppressor(
            source,
            reportSuppressedDiagnostics: true);
        Assert.That(
            includingSuppressed.Any(d => d.Id == "CS0658" && d.IsSuppressed),
            Is.True);
    }

    [Test]
    public async System.Threading.Tasks.Task VersionPropertyTargetSuppressor_DoesNotSuppressOrdinaryField()
    {
        var source = @"
using System;
namespace Test
{
    public class TestClass
    {
        [VersionProperty: Obsolete]
        private int __Health;
    }
}";

        var diagnostics = await GeneratorTestHelper.RunVersionPropertyTargetSuppressor(source);

        Assert.That(
            diagnostics.Any(d => d.Id == "CS0658" && !d.IsSuppressed),
            Is.True);
    }

    [Test]
    public async System.Threading.Tasks.Task VersionPropertyTargetSuppressor_DoesNotSuppressNestedStructField()
    {
        var source = @"
using System;
namespace Test
{
    public partial class Outer
    {
        public partial struct Inner : IVersion
        {
            [VersionField]
            [VersionProperty: Obsolete]
            private int __Health;
        }
    }
}";

        var diagnostics = await GeneratorTestHelper.RunVersionPropertyTargetSuppressor(source);

        Assert.That(
            diagnostics.Any(d => d.Id == "CS0658" && !d.IsSuppressed),
            Is.True);
    }
}
