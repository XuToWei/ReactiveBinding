using NUnit.Framework;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class VersionFieldTests
{
    [Test]
    public void BasicField_GeneratesProperty()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private int m_Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "get => m_Health;");
        GeneratorTestHelper.AssertGeneratedContains(result, "IncrementVersion();");
    }

    [Test]
    public void GeneratesIVersionImplementation()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private int m_Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Should generate IVersion implementation using global VersionCounter
        GeneratorTestHelper.AssertGeneratedContains(result, "private int __version;");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Version => __version;");
        GeneratorTestHelper.AssertGeneratedContains(result, "public void IncrementVersion()");
        GeneratorTestHelper.AssertGeneratedContains(result, "ReactiveBinding.VersionCounter.Next()");
    }

    [Test]
    public void Field_WithoutPrefix_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private int Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF2001");
    }

    [Test]
    public void Class_NotPartial_ReportsError()
    {
        var source = @"
namespace Test
{
    public class TestClass : IVersion
    {
        [VersionField]
        private int m_Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF1001");
    }

    [Test]
    public void Class_NotImplementingInterface_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class TestClass
    {
        [VersionField]
        private int m_Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF1002");
    }

    [Test]
    public void FloatField_UsesEpsilonComparison()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private float m_Speed;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "System.Math.Abs");
        GeneratorTestHelper.AssertGeneratedContains(result, "1e-6f");
    }

    [Test]
    public void DoubleField_UsesEpsilonComparison()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private double m_Position;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "System.Math.Abs");
        GeneratorTestHelper.AssertGeneratedContains(result, "1e-9d");
    }

    [Test]
    public void StringField_UsesDirectComparison()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private string m_Name;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "value != m_Name");
    }

    [Test]
    public void MultipleFields_GeneratesAllProperties()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private int m_Health;

        [VersionField]
        private int m_Mana;

        [VersionField]
        private string m_Name;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Mana");
        GeneratorTestHelper.AssertGeneratedContains(result, "public string Name");
    }

    [Test]
    public void PropertyCollision_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private int m_Health;

        public int Health { get; set; }
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF2003");
    }

    [Test]
    public void Field_NotPrivate_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        public int m_Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF2002");
    }

    [Test]
    public void PropertyName_RemovesPrefixAndCapitalizes()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private int m_playerHealth;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // m_playerHealth -> PlayerHealth
        GeneratorTestHelper.AssertGeneratedContains(result, "public int PlayerHealth");
    }

    [Test]
    public void BoolField_UsesDirectComparison()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private bool m_IsActive;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public bool IsActive");
        GeneratorTestHelper.AssertGeneratedContains(result, "value != m_IsActive");
    }

    [Test]
    public void IncrementVersion_HasCorrectLogic()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private int m_Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Check IncrementVersion increments own version and notifies parent
        GeneratorTestHelper.AssertGeneratedContains(result, "__version = ReactiveBinding.VersionCounter.Next()");
        GeneratorTestHelper.AssertGeneratedContains(result, "if (Parent != null) Parent.IncrementVersion()");
    }

    [Test]
    public void ParentSetter_IsAutoProperty()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private int m_Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Parent is a simple auto-property
        GeneratorTestHelper.AssertGeneratedContains(result, "public ReactiveBinding.IVersion Parent { get; set; }");
    }

    [Test]
    public void NestedIVersionField_PropagatesParent()
    {
        var source = @"
namespace Test
{
    public partial class ChildData : IVersion
    {
        [VersionField]
        private int m_Value;
    }

    public partial class ParentData : IVersion
    {
        [VersionField]
        private ChildData m_Child;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Check that nested IVersion field manages Parent chain
        GeneratorTestHelper.AssertGeneratedContains(result, "if (m_Child != null) m_Child.Parent = null;");
        GeneratorTestHelper.AssertGeneratedContains(result, "if (value != null) value.Parent = this;");
    }

    [Test]
    public void NonIVersionField_DoesNotManageParent()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private string m_Name;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        var generated = GeneratorTestHelper.GetGeneratedForClass(result, "TestClass");

        GeneratorTestHelper.AssertNoErrors(result);
        // Non-IVersion field should not have Parent management
        Assert.That(generated, Does.Not.Contain("m_Name.Parent"));
    }
}
