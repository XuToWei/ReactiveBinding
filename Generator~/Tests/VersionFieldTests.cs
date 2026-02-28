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

    [Test]
    public void TwoLevelNested_ParentChildRelationship()
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

        [VersionField]
        private string m_Name;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);

        // ChildData should have basic IVersion implementation
        var childGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "ChildData");
        Assert.That(childGenerated, Does.Contain("public int Value"));
        Assert.That(childGenerated, Does.Contain("if (Parent != null) Parent.IncrementVersion()"));

        // ParentData should manage Child's Parent
        var parentGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "ParentData");
        Assert.That(parentGenerated, Does.Contain("if (m_Child != null) m_Child.Parent = null;"));
        Assert.That(parentGenerated, Does.Contain("if (value != null) value.Parent = this;"));
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
        private int m_Damage;
    }

    public partial class PlayerData : IVersion
    {
        [VersionField]
        private WeaponData m_Weapon;

        [VersionField]
        private int m_Health;
    }

    public partial class GameData : IVersion
    {
        [VersionField]
        private PlayerData m_Player;

        [VersionField]
        private string m_GameName;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);

        // WeaponData - leaf level
        var weaponGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "WeaponData");
        Assert.That(weaponGenerated, Does.Contain("public int Damage"));
        Assert.That(weaponGenerated, Does.Contain("if (Parent != null) Parent.IncrementVersion()"));

        // PlayerData - middle level, manages WeaponData
        var playerGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "PlayerData");
        Assert.That(playerGenerated, Does.Contain("if (m_Weapon != null) m_Weapon.Parent = null;"));
        Assert.That(playerGenerated, Does.Contain("public int Health"));

        // GameData - root level, manages PlayerData
        var gameGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "GameData");
        Assert.That(gameGenerated, Does.Contain("if (m_Player != null) m_Player.Parent = null;"));
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
        private int m_Count;
    }

    public partial class InventoryData : IVersion
    {
        [VersionField]
        private VersionList<ItemData> m_Items;

        [VersionField]
        private int m_Gold;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);

        // ItemData should have basic IVersion implementation
        var itemGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "ItemData");
        Assert.That(itemGenerated, Does.Contain("public int Count"));

        // InventoryData should manage VersionList's Parent
        var inventoryGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "InventoryData");
        Assert.That(inventoryGenerated, Does.Contain("ReactiveBinding.VersionList<Test.ItemData> Items"));
        Assert.That(inventoryGenerated, Does.Contain("if (m_Items != null) m_Items.Parent = null;"));
        Assert.That(inventoryGenerated, Does.Contain("if (value != null) value.Parent = this;"));
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
        private int m_Level;
    }

    public partial class TeamData : IVersion
    {
        [VersionField]
        private VersionDictionary<string, PlayerData> m_Players;
    }
}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator(source);

        GeneratorTestHelper.AssertNoErrors(result);

        // TeamData should manage VersionDictionary's Parent
        var teamGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "TeamData");
        Assert.That(teamGenerated, Does.Contain("ReactiveBinding.VersionDictionary<string, Test.PlayerData> Players"));
        Assert.That(teamGenerated, Does.Contain("if (m_Players != null) m_Players.Parent = null;"));
        Assert.That(teamGenerated, Does.Contain("if (value != null) value.Parent = this;"));
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
        private int m_Damage;

        [VersionField]
        private float m_CoolDown;
    }

    public partial class CharacterData : IVersion
    {
        [VersionField]
        private int m_Health;

        [VersionField]
        private VersionList<SkillData> m_Skills;
    }

    public partial class GameData : IVersion
    {
        [VersionField]
        private CharacterData m_MainCharacter;

        [VersionField]
        private VersionList<CharacterData> m_AllCharacters;
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
        Assert.That(charGenerated, Does.Contain("if (m_Skills != null) m_Skills.Parent = null;"));

        // GameData - root level with both single and container fields
        var gameGenerated = GeneratorTestHelper.GetGeneratedForClass(result, "GameData");
        Assert.That(gameGenerated, Does.Contain("Test.CharacterData MainCharacter"));
        Assert.That(gameGenerated, Does.Contain("if (m_MainCharacter != null) m_MainCharacter.Parent = null;"));
        Assert.That(gameGenerated, Does.Contain("ReactiveBinding.VersionList<Test.CharacterData> AllCharacters"));
        Assert.That(gameGenerated, Does.Contain("if (m_AllCharacters != null) m_AllCharacters.Parent = null;"));
    }

    [Test]
    public async Task ParentAccess_SetFromNonIVersion_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;
    }

    public class GameManager
    {
        public void DoSomething()
        {
            var player = new PlayerData();
            player.Parent = null;  // Should report VF3001
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF3001"));
    }

    [Test]
    public async Task ParentAccess_GetFromNonIVersion_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;
    }

    public class GameManager
    {
        public void DoSomething()
        {
            var player = new PlayerData();
            var parent = player.Parent;  // Should report VF3001
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF3001"));
    }

    [Test]
    public async Task ParentAccess_FromIVersionImplementation_NoError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;

        public void NotifyParent()
        {
            // Accessing Parent from within IVersion implementation is allowed
            if (Parent != null)
            {
                Parent.IncrementVersion();
            }
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task ParentAccess_NonIVersionType_NoError()
    {
        var source = @"
namespace Test
{
    public class SomeClass
    {
        public object Parent { get; set; }
    }

    public class GameManager
    {
        public void DoSomething()
        {
            var obj = new SomeClass();
            obj.Parent = null;  // Not IVersion, should not report error
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task ParentAccess_MultipleAccessInOneMethod_ReportsMultipleErrors()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;
    }

    public class GameManager
    {
        public void DoSomething()
        {
            var player = new PlayerData();
            player.Parent = null;      // Error 1
            var p = player.Parent;     // Error 2
            player.Parent = p;         // Error 3
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(3));
        Assert.That(diagnostics.All(d => d.Id == "VF3001"), Is.True);
    }

    [Test]
    public async Task ParentAccess_ViaMethodParameter_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;
    }

    public class GameManager
    {
        public void ProcessPlayer(PlayerData player)
        {
            var parent = player.Parent;  // Should report VF3001
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF3001"));
    }

    [Test]
    public async Task ParentAccess_ViaIVersionInterface_ReportsError()
    {
        var source = @"
namespace Test
{
    public class GameManager
    {
        public void ProcessVersion(IVersion version)
        {
            var parent = version.Parent;  // Should report VF3001
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF3001"));
    }

    [Test]
    public async Task ParentAccess_InNestedClassInsideNonIVersion_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;
    }

    public class GameManager
    {
        public class InnerHelper
        {
            public void DoSomething(PlayerData player)
            {
                player.Parent = null;  // Should report VF3001
            }
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF3001"));
    }

    [Test]
    public async Task ParentAccess_InNestedClassInsideIVersion_NoError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;

        public class InnerHelper
        {
            public void NotifyParent(PlayerData player)
            {
                // Nested class inside IVersion is still allowed
                if (player.Parent != null)
                {
                    player.Parent.IncrementVersion();
                }
            }
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task ParentAccess_InLambdaInsideNonIVersion_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;
    }

    public class GameManager
    {
        public void DoSomething()
        {
            var player = new PlayerData();
            System.Action action = () => player.Parent = null;  // Should report VF3001
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF3001"));
    }

    [Test]
    public async Task ParentAccess_InLambdaInsideIVersion_NoError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;

        public void DoSomething()
        {
            System.Action action = () =>
            {
                if (Parent != null) Parent.IncrementVersion();
            };
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task ParentAccess_InPropertyGetterNonIVersion_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;
    }

    public class GameManager
    {
        private PlayerData _player = new PlayerData();

        public IVersion PlayerParent => _player.Parent;  // Should report VF3001
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF3001"));
    }

    [Test]
    public async Task ParentAccess_InConstructorNonIVersion_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;
    }

    public class GameManager
    {
        public GameManager()
        {
            var player = new PlayerData();
            player.Parent = null;  // Should report VF3001
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF3001"));
    }

    [Test]
    public async Task ParentAccess_InStaticMethodNonIVersion_ReportsError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;
    }

    public class GameManager
    {
        public static void ProcessPlayer(PlayerData player)
        {
            player.Parent = null;  // Should report VF3001
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("VF3001"));
    }

    [Test]
    public async Task ParentAccess_InStaticMethodInsideIVersion_NoError()
    {
        var source = @"
namespace Test
{
    public partial class PlayerData : IVersion
    {
        [VersionField]
        private int m_Health;

        public static void ProcessPlayer(PlayerData player)
        {
            // Static method inside IVersion class is allowed
            if (player.Parent != null)
            {
                player.Parent.IncrementVersion();
            }
        }
    }
}";
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(source);

        Assert.That(diagnostics, Has.Length.EqualTo(0));
    }
}
