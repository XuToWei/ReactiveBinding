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
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
        GeneratorTestHelper.AssertGeneratedContains(result, "get => __Health;");
        GeneratorTestHelper.AssertGeneratedContains(result, "__IncrementVersion();");
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
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Should generate IVersion implementation using global VersionCounter
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Version => __Version;");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int __Version { get; set; }");
        GeneratorTestHelper.AssertGeneratedContains(result, "public void __IncrementVersion()");
        GeneratorTestHelper.AssertGeneratedContains(result, "public void Reset() => __Reset();");
        GeneratorTestHelper.AssertGeneratedContains(result, "ReactiveBinding.VersionCounter.Next()");
    }

    [Test]
    public void GeneratesWritableVersionAndUsesInterfaceSyncAliases()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class TestClass : IVersionSync
    {
        [VersionField]
        private int __Health;
    }
}");

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public int __Version { get; set; }");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int __SyncId { get; set; }");
        GeneratorTestHelper.AssertGeneratedContains(result, "public ReactiveBinding.SyncContext __SyncContext { get; set; }");
        GeneratorTestHelper.AssertGeneratedContains(result, "public bool __IsDirty =>");

        var generated = GeneratorTestHelper.GetGeneratedForClass(result, "TestClass");
        Assert.That(generated, Does.Not.Contain("ReactiveBinding.IVersionSync.__Version"));
        Assert.That(generated, Does.Not.Contain("public int SyncId =>"));
        Assert.That(generated, Does.Not.Contain("public ReactiveBinding.SyncContext SyncContext =>"));
        Assert.That(generated, Does.Not.Contain("public bool IsDirty =>"));
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10005");
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
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10001");
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
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10002");
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
        private float __Speed;
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
        private double __Position;
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
        private string __Name;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "EqualityComparer<string>.Default.Equals(value, __Name)");
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
        private int __Health;

        [VersionField]
        private int __Mana;

        [VersionField]
        private string __Name;
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
        private int __Health;

        public int Health { get; set; }
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10007");
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
        public int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10006");
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
        private int __playerHealth;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // __playerHealth -> PlayerHealth
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
        private bool __IsActive;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public bool IsActive");
        GeneratorTestHelper.AssertGeneratedContains(result, "EqualityComparer<bool>.Default.Equals(value, __IsActive)");
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
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Check __IncrementVersion increments own version and notifies parent
        GeneratorTestHelper.AssertGeneratedContains(result, "__Version = ReactiveBinding.VersionCounter.Next()");
        GeneratorTestHelper.AssertGeneratedContains(result, "if (__Parent != null) __Parent.__IncrementVersion()");
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
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // __Parent is a simple auto-property
        GeneratorTestHelper.AssertGeneratedContains(result, "public ReactiveBinding.IVersion __Parent { get; set; }");
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
        private int __Value;
    }

    public partial class ParentData : IVersion
    {
        [VersionField]
        private ChildData __Child;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        // Check that nested IVersion field manages __Parent chain
        GeneratorTestHelper.AssertGeneratedContains(result, "if (__Child != null) __Child.__Parent = null;");
        GeneratorTestHelper.AssertGeneratedContains(result, "if (value != null) value.__Parent = this;");
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
        private string __Name;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);
        var generated = GeneratorTestHelper.GetGeneratedForClass(result, "TestClass");

        GeneratorTestHelper.AssertNoErrors(result);
        // Non-IVersion field should not have __Parent management
        Assert.That(generated, Does.Not.Contain("__Name.__Parent"));
    }

    [Test]
    public void TwoLevelNested_ParentChildRelationship()
    {
        var source = @"
namespace Test
{
    public partial class ChildData : IVersion
    {
        [VersionField]
        private int __Value;
    }

    public partial class ParentData : IVersion
    {
        [VersionField]
        private ChildData __Child;

        [VersionField]
        private string __Name;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);

        // ChildData should have basic IVersion implementation
        var childGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "ChildData");
        Assert.That(childGenerated, Does.Contain("public int Value"));
        Assert.That(childGenerated, Does.Contain("if (__Parent != null) __Parent.__IncrementVersion()"));

        // ParentData should manage Child's __Parent
        var parentGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "ParentData");
        Assert.That(parentGenerated, Does.Contain("if (__Child != null) __Child.__Parent = null;"));
        Assert.That(parentGenerated, Does.Contain("if (value != null) value.__Parent = this;"));
    }

    [Test]
    public void ThreeLevelNested_RootParentChildRelationship()
    {
        var source = @"
namespace Test
{
    public partial class WeaponData : IVersion
    {
        [VersionField]
        private int __Damage;
    }

    public partial class PlayerData : IVersion
    {
        [VersionField]
        private WeaponData __Weapon;

        [VersionField]
        private int __Health;
    }

    public partial class GameData : IVersion
    {
        [VersionField]
        private PlayerData __Player;

        [VersionField]
        private string __GameName;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);

        // WeaponData - leaf level
        var weaponGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "WeaponData");
        Assert.That(weaponGenerated, Does.Contain("public int Damage"));
        Assert.That(weaponGenerated, Does.Contain("if (__Parent != null) __Parent.__IncrementVersion()"));

        // PlayerData - middle level, manages WeaponData
        var playerGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "PlayerData");
        Assert.That(playerGenerated, Does.Contain("if (__Weapon != null) __Weapon.__Parent = null;"));
        Assert.That(playerGenerated, Does.Contain("public int Health"));

        // GameData - root level, manages PlayerData
        var gameGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "GameData");
        Assert.That(gameGenerated, Does.Contain("if (__Player != null) __Player.__Parent = null;"));
        Assert.That(gameGenerated, Does.Contain("public string GameName"));
    }

    [Test]
    public void VersionListField_ManagesParentChain()
    {
        var source = @"
namespace Test
{
    public partial class ItemData : IVersion
    {
        [VersionField]
        private int __Count;
    }

    public partial class InventoryData : IVersion
    {
        [VersionField]
        private VersionList<ItemData> __Items;

        [VersionField]
        private int __Gold;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);

        // ItemData should have basic IVersion implementation
        var itemGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "ItemData");
        Assert.That(itemGenerated, Does.Contain("public int Count"));

        // InventoryData should manage VersionList's __Parent
        var inventoryGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "InventoryData");
        Assert.That(inventoryGenerated, Does.Contain("ReactiveBinding.VersionList<Test.ItemData> Items"));
        Assert.That(inventoryGenerated, Does.Contain("if (__Items != null) __Items.__Parent = null;"));
        Assert.That(inventoryGenerated, Does.Contain("if (value != null) value.__Parent = this;"));
    }

    [Test]
    public void VersionDictionaryField_ManagesParentChain()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Level;
    }

    public partial class TeamData : IVersion
    {
        [VersionField]
        private VersionDictionary<string, PlayerData> __Players;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);

        // TeamData should manage VersionDictionary's __Parent
        var teamGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "TeamData");
        Assert.That(teamGenerated, Does.Contain("ReactiveBinding.VersionDictionary<string, Test.PlayerData> Players"));
        Assert.That(teamGenerated, Does.Contain("if (__Players != null) __Players.__Parent = null;"));
        Assert.That(teamGenerated, Does.Contain("if (value != null) value.__Parent = this;"));
    }

    [Test]
    public void ComplexHierarchy_ThreeLevelWithContainer()
    {
        var source = @"
namespace Test
{
    public partial class SkillData : IVersion
    {
        [VersionField]
        private int __Damage;

        [VersionField]
        private float __CoolDown;
    }

    public partial class CharacterData : IVersion
    {
        [VersionField]
        private int __Health;

        [VersionField]
        private VersionList<SkillData> __Skills;
    }

    public partial class GameData : IVersion
    {
        [VersionField]
        private CharacterData __MainCharacter;

        [VersionField]
        private VersionList<CharacterData> __AllCharacters;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);

        // SkillData - leaf level
        var skillGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "SkillData");
        Assert.That(skillGenerated, Does.Contain("public int Damage"));
        Assert.That(skillGenerated, Does.Contain("public float CoolDown"));

        // CharacterData - middle level with container
        var charGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "CharacterData");
        Assert.That(charGenerated, Does.Contain("public int Health"));
        Assert.That(charGenerated, Does.Contain("ReactiveBinding.VersionList<Test.SkillData> Skills"));
        Assert.That(charGenerated, Does.Contain("if (__Skills != null) __Skills.__Parent = null;"));

        // GameData - root level with both single and container fields
        var gameGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "GameData");
        Assert.That(gameGenerated, Does.Contain("Test.CharacterData MainCharacter"));
        Assert.That(gameGenerated, Does.Contain("if (__MainCharacter != null) __MainCharacter.__Parent = null;"));
        Assert.That(gameGenerated, Does.Contain("ReactiveBinding.VersionList<Test.CharacterData> AllCharacters"));
        Assert.That(gameGenerated, Does.Contain("if (__AllCharacters != null) __AllCharacters.__Parent = null;"));
    }

    // ===== VF10010: Direct VersionField access tests =====

    [Test]
    public async Task DirectFieldAccess_ReadInMethod_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health;

        public int GetHealthDirect()
        {
            return __Health;  // Should report VF10010
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF10010"));
    }

    [Test]
    public async Task DirectFieldAccess_WriteInMethod_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health;

        public void SetHealthDirect(int value)
        {
            __Health = value;  // Should report VF10010
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF10010"));
    }

    [Test]
    public async Task DirectFieldAccess_InConstructor_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health;

        public PlayerData()
        {
            __Health = 100;  // Should report VF10010
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF10010"));
    }

    [Test]
    public async Task DirectFieldAccess_MultipleAccesses_ReportsMultipleErrors()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health;

        public void DoSomething()
        {
            var h = __Health;   // Error 1
            __Health = h + 1;   // Error 2
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(2));
        Assert.That(diagnostics.All(d => d.Id == "VF10010"), Is.True);
    }

    [Test]
    public async Task DirectFieldAccess_FieldWithoutAttribute_NoError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health;

        private int __InternalCounter;

        public void DoSomething()
        {
            __InternalCounter = 42;  // Not a VersionField, no error
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task DirectFieldAccess_InLambda_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health;

        public void DoSomething()
        {
            System.Action action = () => __Health = 50;  // Should report VF10010
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF10010"));
    }

    [Test]
    public async Task DirectFieldAccess_InPropertyGetter_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health;

        public bool IsAlive => __Health > 0;  // Should report VF10010
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF10010"));
    }

    // ===== VF10011: VersionField initializer tests =====

    [Test]
    public async Task FieldWithInitializer_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health = 100;
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldInitializerAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF10011"));
    }

    [Test]
    public async Task FieldWithoutInitializer_NoError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health;
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldInitializerAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task FieldWithInitializer_NonVersionField_NoError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        private int __Health = 100;
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldInitializerAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task MultipleFieldsWithInitializers_ReportsMultipleErrors()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int __Health = 100;

        [VersionField]
        private string __Name = ""default"";
    }
}";
        var diagnostics = await GeneratorTestHelper.RunVersionFieldInitializerAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(2));
        Assert.That(diagnostics.All(d => d.Id == "VF10011"), Is.True);
    }

    [Test]
    public void NestedClass_GeneratesWithOuterClassWrapper()
    {
        var source = @"
namespace Test
{
    public partial class OuterClass
    {
        public partial class InnerClass : IVersion
        {
            [VersionField]
            private int __Health;
        }
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "partial class OuterClass");
        GeneratorTestHelper.AssertGeneratedContains(result, "partial class InnerClass");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
    }

    [Test]
    public void DeepNestedClass_GeneratesWithAllOuterClassWrappers()
    {
        var source = @"
namespace Test
{
    public partial class Level1
    {
        public partial class Level2
        {
            public partial class Level3 : IVersion
            {
                [VersionField]
                private int __Value;
            }
        }
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "partial class Level1");
        GeneratorTestHelper.AssertGeneratedContains(result, "partial class Level2");
        GeneratorTestHelper.AssertGeneratedContains(result, "partial class Level3");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Value");
    }

    [Test]
    public void PropertyAttributes_SingleAttribute_GeneratesAttributeOnProperty()
    {
        var source = @"
using System;
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionFieldProperty(typeof(ObsoleteAttribute))]
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "[System.ObsoleteAttribute]");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
    }

    [Test]
    public void PropertyAttributes_MultipleAttributes_GeneratesAllAttributesOnProperty()
    {
        var source = @"
using System;
namespace Test
{
    public class MyCustomAttribute : Attribute { }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionFieldProperty(typeof(ObsoleteAttribute))]
        [VersionFieldProperty(typeof(MyCustomAttribute))]
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "[System.ObsoleteAttribute]");
        GeneratorTestHelper.AssertGeneratedContains(result, "[Test.MyCustomAttribute]");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
    }

    [Test]
    public void PropertyAttributes_NoAttributes_GeneratesPropertyWithoutAttributes()
    {
        var source = @"
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
    }

    [Test]
    public void PropertyAttributes_WithNamespace_GeneratesFullyQualifiedAttribute()
    {
        var source = @"
using System;
namespace MyLib.Annotations
{
    public class SpecialAttribute : Attribute { }
}
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionFieldProperty(typeof(MyLib.Annotations.SpecialAttribute))]
        private string __Name;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "[MyLib.Annotations.SpecialAttribute]");
        GeneratorTestHelper.AssertGeneratedContains(result, "public string Name");
    }

    [Test]
    public void PropertyAttributes_MultipleFields_EachFieldGetsOwnAttributes()
    {
        var source = @"
using System;
namespace Test
{
    public class TagAttribute : Attribute { }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionFieldProperty(typeof(ObsoleteAttribute))]
        private int __Health;

        [VersionField]
        [VersionFieldProperty(typeof(TagAttribute))]
        private string __Name;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "[System.ObsoleteAttribute]");
        GeneratorTestHelper.AssertGeneratedContains(result, "[Test.TagAttribute]");
    }

    [Test]
    public void PropertyAttributes_WithoutAttributeSuffix_GeneratesCorrectly()
    {
        var source = @"
using System;
namespace Test
{
    public class HideInInspector : Attribute { }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionFieldProperty(typeof(HideInInspector))]
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "[Test.HideInInspector]");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
    }

    [Test]
    public void PropertyAttributes_StringText_GeneratesVerbatim()
    {
        var source = @"
using System;
namespace Test
{
    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionFieldProperty(""System.Obsolete(\""Use NewHealth\"")"")]
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, @"[System.Obsolete(""Use NewHealth"")]");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
    }

    [Test]
    public void PropertyAttributes_MixedTypeAndString_GeneratesAll()
    {
        var source = @"
using System;
namespace Test
{
    public class TagAttribute : Attribute { }

    public partial class TestClass : IVersion
    {
        [VersionField]
        [VersionFieldProperty(typeof(TagAttribute))]
        [VersionFieldProperty(""System.Obsolete(\""deprecated\"")"")]
        private int __Health;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "[Test.TagAttribute]");
        GeneratorTestHelper.AssertGeneratedContains(result, @"[System.Obsolete(""deprecated"")]");
        GeneratorTestHelper.AssertGeneratedContains(result, "public int Health");
    }
}
