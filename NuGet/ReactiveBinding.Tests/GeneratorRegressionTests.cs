using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using ReactiveBinding;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class GeneratorRegressionTests
{
    [Test]
    public void VersionFieldInheritance_IsRejectedWithVF10003()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class Base : IVersion
    {
        [VersionField] private int __BaseValue;
    }

    public partial class Derived : Base
    {
        [VersionField] private int __DerivedValue;
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10003");
        Assert.That(result.GeneratedSources.Count(s => s.Contains("partial class Derived")), Is.Zero);
    }

    [Test]
    public async System.Threading.Tasks.Task VersionInheritanceWithoutFields_IsRejectedWithVF10003()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionInheritanceAnalyzer(@"
namespace Test
{
    public class Base : IVersion
    {
        public int __Version { get; set; }
        public IVersion __Parent { get; set; }
        public void __IncrementVersion() { }
        public void __Reset() { }
    }

    public class Derived : Base { }
}");

        Assert.That(diagnostics, Has.Exactly(1).Matches<Diagnostic>(d => d.Id == "VF10003"));
    }

    [Test]
    public void GeneratedReferenceSetter_RejectsSameInstanceInTwoFields()
    {
        var compiled = GeneratorTestHelper.CompileAndRun(@"
namespace Test
{
    public partial class Child : IVersion
    {
        [VersionField] private int __Value;
    }

    public partial class Parent : IVersion
    {
        [VersionField] private Child __Left;
        [VersionField] private Child __Right;
    }
}");
        dynamic parent = compiled.Create("Test.Parent");
        dynamic child = compiled.Create("Test.Child");
        parent.Left = child;

        Assert.Throws<InvalidOperationException>(() => parent.Right = child);
        Assert.That((object)parent.Left, Is.SameAs((object)child));
        Assert.That((object)parent.Right, Is.Null);
    }

    [Test]
    public void Apply_AdvancesConsumerVersions()
    {
        var compiled = GeneratorTestHelper.CompileAndRun(@"
namespace Test
{
    public partial class Bag : IVersionSync
    {
        [VersionField] private int __Value;
        [VersionField] private VersionSyncList<int> __Items;
    }
}");
        dynamic producer = compiled.Create("Test.Bag");
        dynamic consumer = compiled.Create("Test.Bag");
        producer.Items = new VersionSyncList<int>();
        consumer.Items = new VersionSyncList<int>();
        var producerContext = new SyncContext();
        var consumerContext = new SyncContext();
        ((IVersionSync)producer).AttachTo(producerContext);
        ((IVersionSync)consumer).AttachTo(consumerContext);

        Apply(consumerContext, Full(producerContext));
        var rootBefore = (int)consumer.__Version;
        var listBefore = (int)consumer.Items.__Version;
        producer.Value = 7;
        producer.Items.Add(9);
        Apply(consumerContext, Delta(producerContext));

        Assert.That((int)consumer.__Version, Is.GreaterThan(rootBefore));
        Assert.That((int)consumer.Items.__Version, Is.GreaterThan(listBefore));
    }

    [Test]
    public void Apply_DoesNotCreateOutboundDirtyState()
    {
        var compiled = GeneratorTestHelper.CompileAndRun(@"
namespace Test
{
    public partial class Bag : IVersionSync
    {
        [VersionField] private int __Value;
        [VersionField] private VersionSyncList<int> __Items;
    }
}");
        dynamic producer = compiled.Create("Test.Bag");
        dynamic consumer = compiled.Create("Test.Bag");
        producer.Items = new VersionSyncList<int>();
        consumer.Items = new VersionSyncList<int>();
        var producerContext = new SyncContext();
        var consumerContext = new SyncContext();
        ((IVersionSync)producer).AttachTo(producerContext);
        ((IVersionSync)consumer).AttachTo(consumerContext);

        producer.Value = 12;
        producer.Items.Add(3);
        Apply(consumerContext, Full(producerContext));

        Assert.That(Delta(consumerContext), Is.EqualTo(new byte[] { 0, 0, 0 }));
    }

    [Test]
    public void GenericReactiveType_GeneratesCompilablePartialDeclaration()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial class Box<T> : IReactiveObserver where T : class
    {
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed() { }
    }
}");

        AssertNoCompilationErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "partial class Box<T>");
        GeneratorTestHelper.AssertGeneratedContains(result, "where T : class");
    }

    [Test]
    public void ReactiveHintNames_DistinguishGenericArityFromLiteralSuffix()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial class Box<T> : IReactiveObserver
    {
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed() { }
    }

    public partial class Box_1 : IReactiveObserver
    {
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed() { }
    }
}");

        AssertNoCompilationErrors(result);
        Assert.That(result.GeneratedSources, Has.Exactly(2).Items);
    }

    [Test]
    public void VersionFieldHintNames_DistinguishGenericArityFromLiteralSuffix()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class Node<T> : IVersion
    {
        [VersionField] private int __Value;
    }

    public partial class Node_1 : IVersion
    {
        [VersionField] private int __Value;
    }
}");

        AssertNoCompilationErrors(result);
        Assert.That(result.GeneratedSources, Has.Exactly(2).Items);
    }

    [Test]
    public void NullableSyncReference_GeneratesCompilableConstruction()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
#nullable enable
namespace Test
{
    public partial class Child : IVersionSync
    {
        [VersionField] private int __Value;
    }

    public partial class Parent : IVersionSync
    {
        [VersionField] private Child? __Child;
    }
}");

        AssertNoCompilationErrors(result);
        Assert.That(result.GeneratedSources, Has.None.Contains("new Test.Child?()"));
    }

    [Test]
    public void VersionField_NaNAssignmentIsNotSuppressed()
    {
        var compiled = GeneratorTestHelper.CompileAndRun(@"
namespace Test
{
    public partial class Data : IVersion
    {
        [VersionField] private float __Value;
    }
}");
        dynamic data = compiled.Create("Test.Data");

        data.Value = float.NaN;

        Assert.That(float.IsNaN((float)data.Value), Is.True);
        Assert.That((int)data.__Version, Is.GreaterThan(0));
    }

    [Test]
    public void QualifiedReactiveBindLiteral_StillRequiresNameof()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] private int Value;
        [ReactiveBinding.ReactiveBind(""Value"")] private void Changed() { }
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10022");
    }

    [Test]
    public void AutoInference_DoesNotTreatOtherInstanceAsThis()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] public int Value;
        private Observer Other;
        [ReactiveBind] private void Changed() { System.Console.WriteLine(Other.Value); }
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10023");
    }

    [Test]
    public void ApplyVersionChange_TriggersReactiveBind()
    {
        var compiled = GeneratorTestHelper.CompileAndRunAll(@"
namespace Test
{
    public partial class Bag : IVersionSync
    {
        [VersionField] private int __Value;
    }

    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] public Bag Data;
        public int Calls;
        [ReactiveBind(nameof(Data))] private void Changed(Bag value) { Calls++; }
    }
}");
        dynamic producer = compiled.Create("Test.Bag");
        dynamic consumer = compiled.Create("Test.Bag");
        var producerContext = new SyncContext();
        var consumerContext = new SyncContext();
        ((IVersionSync)producer).AttachTo(producerContext);
        ((IVersionSync)consumer).AttachTo(consumerContext);
        Apply(consumerContext, Full(producerContext));

        dynamic observer = compiled.Create("Test.Observer");
        observer.Data = consumer;
        observer.ObserveChanges();
        producer.Value = 10;
        Apply(consumerContext, Delta(producerContext));
        observer.ObserveChanges();

        Assert.That((int)observer.Calls, Is.EqualTo(2));
    }

    [Test]
    public void ReactiveSourceAccessor_IsEvaluatedOncePerObservation()
    {
        var compiled = GeneratorTestHelper.CompileAndRunReactive(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        public int Reads;
        public int Value = 7;
        public int CallbackValue;
        [ReactiveSource] private int GetValue() { Reads++; return Value; }
        [ReactiveBind(nameof(GetValue))] private void Changed(int value) { CallbackValue = value; }
    }
}");
        dynamic observer = compiled.Create("Test.Observer");

        observer.ObserveChanges();

        Assert.That((int)observer.Reads, Is.EqualTo(1));
        Assert.That((int)observer.CallbackValue, Is.EqualTo(7));
    }

    [Test]
    public void ReplacingVersionSourceWithSameVersion_TriggersCallback()
    {
        var compiled = GeneratorTestHelper.CompileAndRunReactive(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] public VersionList<int> Items = new VersionList<int>();
        public int Calls;
        [ReactiveBind(nameof(Items))] private void Changed(VersionList<int> value) { Calls++; }
    }
}");
        dynamic observer = compiled.Create("Test.Observer");
        observer.ObserveChanges();

        observer.Items = new VersionList<int>();
        observer.ObserveChanges();

        Assert.That((int)observer.Calls, Is.EqualTo(2));
    }

    [Test]
    public void ReactiveSource_NaNTransitionTriggersCallback()
    {
        var compiled = GeneratorTestHelper.CompileAndRunReactive(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] public float Value;
        public int Calls;
        [ReactiveBind(nameof(Value))] private void Changed(float value) { Calls++; }
    }
}");
        dynamic observer = compiled.Create("Test.Observer");
        observer.ObserveChanges();
        observer.Value = float.NaN;

        observer.ObserveChanges();

        Assert.That((int)observer.Calls, Is.EqualTo(2));
    }

    [Test]
    public void GeneratedReactiveNames_DoNotCollideWithSources()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] private int initialized;
        [ReactiveSource] private VersionList<int> X;
        [ReactiveSource] private int X_version;
        [ReactiveBind(nameof(initialized), nameof(X), nameof(X_version))]
        private void Changed() { }
    }
}");

        AssertNoCompilationErrors(result);
    }

    [Test]
    public void GenericNullableAndNotNullConstraints_ArePreserved()
    {
        var reactive = GeneratorTestHelper.RunGenerator(@"
#nullable enable
namespace Test
{
    public partial class Observer<TRef, TKey> : IReactiveObserver
        where TRef : class?
        where TKey : notnull
    {
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed() { }
    }
}");

        AssertNoCompilationErrors(reactive);
        GeneratorTestHelper.AssertGeneratedContains(reactive, "where TRef : class?");
        GeneratorTestHelper.AssertGeneratedContains(reactive, "where TKey : notnull");
    }

    [Test]
    public void ReactiveClassNestedInRecord_PreservesContainingDeclarationKind()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial record Outer
    {
        public partial class Observer : IReactiveObserver
        {
            [ReactiveSource] private int Value;
            [ReactiveBind(nameof(Value))] private void Changed() { }
        }
    }
}");

        AssertNoCompilationErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "partial record Outer");
    }

    [Test]
    public void NestedReactiveClass_RequiresPartialContainingType()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public class Outer
    {
        public partial class Observer : IReactiveObserver
        {
            [ReactiveSource] private int Value;
            [ReactiveBind(nameof(Value))] private void Changed() { }
        }
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10001");
        Assert.That(result.GeneratedSources, Is.Empty);
    }

    [Test]
    public void NestedVersionClass_RequiresPartialContainingType()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public class Outer
    {
        public partial class Data : IVersion
        {
            [VersionField] private int __Value;
        }
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10001");
        Assert.That(result.GeneratedSources, Is.Empty);
    }

    [TestCase("ref int value")]
    [TestCase("out int value")]
    public void RefOrOutCallback_IsRejected(string parameter)
    {
        var result = GeneratorTestHelper.RunGenerator($@"
namespace Test
{{
    public partial class Observer : IReactiveObserver
    {{
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed({parameter}) {{ value = 0; }}
    }}
}}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10026");
    }

    [Test]
    public void GenericCallback_IsRejected()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed<T>(int value) { }
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10026");
    }

    [Test]
    public void DuplicateReactiveSourceIdentifier_ReportsDiagnosticInsteadOfCrashing()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] private int Value() => 1;
        [ReactiveSource] private int Value<T>() => 2;
        [ReactiveBind(nameof(Value))] private void Changed() { }
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10015");
        Assert.That(result.CompilationDiagnostics, Has.None.Matches<Diagnostic>(d => d.Id == "CS8785"));
    }

    [TestCase("readonly")]
    [TestCase("static")]
    public void UnsupportedVersionFieldModifier_IsRejected(string modifier)
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator($@"
namespace Test
{{
    public partial class Data : IVersion
    {{
        [VersionField] private {modifier} int __Value;
    }}
}}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10008");
    }

    [Test]
    public void GeneratedVersionPropertyCollision_WithMethodIsRejected()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class Data : IVersion
    {
        [VersionField] private int __Value;
        public void Value() { }
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10007");
    }

    [Test]
    public void DuplicateGeneratedVersionProperty_IsRejected()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class Data : IVersion
    {
        [VersionField] private int __value;
        [VersionField] private int __Value;
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10007");
    }

    [Test]
    public void InvalidGeneratedVersionPropertyIdentifier_IsRejected()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class Data : IVersion
    {
        [VersionField] private int __1value;
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10009");
    }

    [Test]
    public void GenericVersionType_GeneratesCompilablePartialDeclaration()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class Data<T> : IVersion where T : class
    {
        [VersionField] private int __Value;
    }
}");

        AssertNoCompilationErrors(result);
        GeneratorTestHelper.AssertGeneratedContains(result, "partial class Data<T>");
    }

    [Test]
    public void VersionProtocol_SupportedDiagnostics_OnlyContainsUnifiedRule()
    {
        var analyzer = new ReactiveBinding.Generator.VersionProtocolAccessAnalyzer();

        Assert.That(
            analyzer.SupportedDiagnostics.Select(descriptor => descriptor.Id),
            Is.EqualTo(new[] { "VF10012" }));
    }

    [Test]
    public async System.Threading.Tasks.Task VersionProtocol_AllInterfaceAccessShapes_AreRejectedOnce()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionProtocolAccessAnalyzer(@"
namespace Test
{
    public class Consumer
    {
        public void Read(IVersion value, IVersionSync sync, VersionSyncList<int> concrete)
        {
            _ = value.__Version;
            value.__Version = 1;
            _ = value.__Parent;
            value.__Parent = null;
            _ = value?.__Parent;
            value.__IncrementVersion();
            value?.__Reset();
            System.Action reset = value.__Reset;
            _ = sync.__SyncId;
            sync.__ClearDirty();
            _ = concrete.__IsDirty;
        }
    }
}");

        Assert.That(diagnostics, Has.Length.EqualTo(11));
        Assert.That(diagnostics.All(d => d.Id == "VF10012"), Is.True);
    }

    [Test]
    public async System.Threading.Tasks.Task VersionProtocol_SyncVersionWritesAndReads_UseUnifiedDiagnostic()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionProtocolAccessAnalyzer(@"
namespace Test
{
    public class Consumer
    {
        public int Mutate(IVersionSync value, VersionSyncList<int> concrete)
        {
            int other = 0;
            value.__Version = 1;
            value.__Version += 1;
            value.__Version++;
            --value.__Version;
            (value.__Version, other) = (3, 4);
            ((IVersionSync)concrete).__Version = 2;
            return value.__Version;
        }
    }
}");

        Assert.That(diagnostics, Has.Length.EqualTo(7));
        Assert.That(diagnostics.All(d => d.Id == "VF10012"), Is.True);
    }

    [Test]
    public async System.Threading.Tasks.Task VersionProtocol_UserImplementationCannotUseImplicitOrConcreteReservedMembers()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionProtocolAccessAnalyzer(@"
namespace Test
{
    public class Manual : IVersion
    {
        public int __Version { get; set; }
        public IVersion __Parent { get; set; }
        public int __Custom;
        public void __IncrementVersion() { }
        public void __Reset() { }
        public void __CustomMethod() { }

        public void Touch()
        {
            _ = __Version;
            _ = __Custom;
            __CustomMethod();
        }
    }

    public abstract class ManualSync : IVersionSync
    {
        public int __Version { get; set; }

        public void Touch()
        {
            __Version = 1;
            this.__Version++;
        }
    }
}");

        Assert.That(diagnostics, Has.Length.EqualTo(5));
        Assert.That(diagnostics.All(d => d.Id == "VF10012"), Is.True);
    }

    [Test]
    public async System.Threading.Tasks.Task VersionProtocol_GenericAndInheritedInterfaceImplementations_AreRejected()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionProtocolAccessAnalyzer(@"
namespace Test
{
    public class Base
    {
        public int __Version { get; set; }
        public IVersion __Parent { get; set; }
        public void __IncrementVersion() { }
        public void __Reset() { }
    }

    public class Node : Base, IVersion { }

    public class Consumer
    {
        public int Read(Node node) => node.__Version;
        public void Reset<T>(T value) where T : IVersion => value.__Reset();
    }
}");

        Assert.That(diagnostics.Count(d => d.Id == "VF10012"), Is.EqualTo(2));
    }

    [Test]
    public async System.Threading.Tasks.Task VersionProtocol_PropertyPatternAndContainerInit_AreRejected()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionProtocolAccessAnalyzer(@"
namespace Test
{
    public class Consumer
    {
        public bool Match(IVersion value) => value is { __Version: > 0 };

        public void Configure(VersionSyncList<int> list)
        {
            list.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        }
    }
}");

        Assert.That(diagnostics.Count(d => d.Id == "VF10012"), Is.EqualTo(2));
    }

    [Test]
    public async System.Threading.Tasks.Task VersionProtocol_NameofUnrelatedAndVersionFieldBackingAccess_AreNotClaimed()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionProtocolAccessAnalyzer(@"
namespace Test
{
    public class Plain
    {
        public int __Version;
        public object __Parent { get; set; }
        public void __Reset() { }
    }

    public partial class Data : IVersion
    {
        [VersionField] private int __Value;

        public void Touch(IVersion version, Plain plain)
        {
            _ = nameof(version.__Version);
            _ = nameof(version.__Parent);
            _ = nameof(version.__Reset);
            plain.__Version = 1;
            plain.__Parent = null;
            _ = plain.__Parent;
            plain.__Reset();
            __Value = 2;
        }
    }
}");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async System.Threading.Tasks.Task VersionProtocol_OnlyExactRuntimeTypesAndTheirNestedTypesAreAllowed()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionProtocolAccessAnalyzer(@"
namespace ReactiveBinding
{
    public interface IVersion
    {
        int __Version { get; }
        IVersion __Parent { get; set; }
        void __IncrementVersion();
        void __Reset();
    }

    public class VersionOwnership
    {
        public void Use(IVersion value) { _ = value.__Parent; value.__Reset(); }
    }

    public class SyncContext
    {
        public void Use(IVersion value) { _ = value.__Version; value.__Reset(); }
    }

    public class VersionList<T> : IVersion
    {
        public int __Version => 0;
        public IVersion __Parent { get; set; }
        public void __IncrementVersion() { }
        public void __Reset() { }
        public void Use(IVersion value) { _ = value.__Version; value.__Reset(); }
        public class Nested { public void Use(IVersion value) => value.__Reset(); }
    }

    public class DerivedContext : SyncContext
    {
        public void UseAgain(IVersion value) => value.__Reset();
    }

    public class DerivedList<T> : VersionList<T>
    {
        public void UseAgain(IVersion value) => value.__Reset();
    }
}

namespace Other
{
    public class VersionOwnership
    {
        public void Use(ReactiveBinding.IVersion value) { value.__Parent = null; }
    }

    public class VersionList<T>
    {
        public object Read(ReactiveBinding.IVersion value) => value?.__Parent;
    }

    public class SyncContext
    {
        public void Use(ReactiveBinding.IVersion value) => value.__Reset();
    }
}", includeUsings: false, includeRuntimeReference: false);

        Assert.That(diagnostics, Has.Length.EqualTo(5));
        Assert.That(diagnostics.All(d => d.Id == "VF10012"), Is.True);
    }

    [Test]
    public async System.Threading.Tasks.Task VersionProtocol_DefaultInterfaceForwardersAreAllowed()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionProtocolAccessAnalyzer(@"
namespace ReactiveBinding
{
    public interface IVersion
    {
        int __Version { get; set; }
        IVersion __Parent { get; set; }
        void __IncrementVersion();
        void __Reset();
    }

    public class SyncContext { }

    public interface IVersionSync : IVersion
    {
        int SyncId => __SyncId;
        SyncContext SyncContext => __SyncContext;
        bool IsDirty => __IsDirty;
        int __SyncId { get; set; }
        SyncContext __SyncContext { get; set; }
        bool __IsDirty { get; }
    }
}", includeUsings: false, includeRuntimeReference: false);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async System.Threading.Tasks.Task VersionProtocol_GeneratedImplementationHasNoDiagnostics()
    {
        var diagnostics = await GeneratorTestHelper.RunGeneratedVersionProtocolAccessAnalyzer(@"
namespace Test
{
    public partial class Data : IVersionSync
    {
        [VersionField] private int __Value;
        [VersionField] private VersionSyncList<int> __Items;
    }
}");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async System.Threading.Tasks.Task EscapedBackingFieldAccess_IsRejected()
    {
        var diagnostics = await GeneratorTestHelper.RunVersionFieldAccessAnalyzer(@"
namespace Test
{
    public partial class Data : IVersion
    {
        [VersionField] private int @__value;
        public void Write() { @__value = 1; }
        public int __Version { get; set; }
        public IVersion __Parent { get; set; }
        public void __IncrementVersion() { }
        public void __Reset() { }
    }
}");

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(d => d.Id == "VF10010"));
    }

    [Test]
    public async System.Threading.Tasks.Task CallingObserveChangesOnOtherInstance_DoesNotSilenceWarning()
    {
        var diagnostics = await GeneratorTestHelper.RunObserveChangesCallAnalyzer(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed() { }
        public void Update(Observer other) { other.ObserveChanges(); }
    }
}");

        var diagnostic = diagnostics.Single(d => d.Id == "RB10009");
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
    }

    [Test]
    public async System.Threading.Tasks.Task DeadLambdaObserveChanges_DoesNotSilenceWarning()
    {
        var diagnostics = await GeneratorTestHelper.RunObserveChangesCallAnalyzer(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed() { }
        public void Start() { System.Action action = () => ObserveChanges(); }
    }
}");

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(d => d.Id == "RB10009"));
    }

    [Test]
    public void InterfaceTypedVersionField_PreservesOwnership()
    {
        var compiled = GeneratorTestHelper.CompileAndRun(@"
namespace Test
{
    public partial class Child : IVersion
    {
        [VersionField] private int __Value;
    }

    public partial class Parent : IVersion
    {
        [VersionField] private IVersion __Child;
    }
}");
        dynamic parent = compiled.Create("Test.Parent");
        dynamic child = compiled.Create("Test.Child");

        parent.Child = child;

        Assert.That((object)((IVersion)child).__Parent, Is.SameAs((object)parent));
    }

    [Test]
    public void InterfaceTypedReactiveSource_UsesVersionAndIdentity()
    {
        var compiled = GeneratorTestHelper.CompileAndRunAll(@"
namespace Test
{
    public partial class Child : IVersion
    {
        [VersionField] private int __Value;
    }

    [ReactiveObserveIgnore]
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] public IVersion Current;
        public int Calls;
        [ReactiveBind(nameof(Current))] private void Changed(IVersion value) { Calls++; }
    }
}");
        dynamic observer = compiled.Create("Test.Observer");
        observer.Current = compiled.Create("Test.Child");
        observer.ObserveChanges();
        observer.Current = compiled.Create("Test.Child");
        observer.ObserveChanges();

        Assert.That((int)observer.Calls, Is.EqualTo(2));
    }

    [Test]
    public void ObjectReactiveSource_IsRejected()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial class Observer : IReactiveObserver
    {
        [ReactiveSource] private object Value;
        [ReactiveBind(nameof(Value))] private void Changed(object value) { }
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB10013");
    }

    [Test]
    public void SealedReactiveRoot_DoesNotGenerateVirtualMethods()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public sealed partial class Observer : IReactiveObserver
    {
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed() { }
    }
}");

        GeneratorTestHelper.AssertGeneratedContains(result, "public void ObserveChanges()");
        Assert.That(result.GeneratedSources.Single(), Does.Not.Contain("public virtual void ObserveChanges()"));
    }

    [Test]
    public void VersionFieldReservedGeneratedMember_IsRejected()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class Data : IVersionSync
    {
        [VersionField] private int __dirtyMask0;
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10004");
        Assert.That(result.GeneratedSources, Is.Empty);
    }

    [TestCase("__wScalar_user")]
    [TestCase("__rScalar_user")]
    [TestCase("__newSync_user")]
    public void VersionFieldNewSyncGeneratedMembers_AreReserved(string memberName)
    {
        string member = $"private void {memberName}() {{ }}";
        var result = GeneratorTestHelper.RunVersionFieldGenerator($@"
namespace Test
{{
    public partial class Data : IVersionSync
    {{
        [VersionField] private int __Value;
        {member}
    }}
}}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10004");
        Assert.That(result.GeneratedSources, Is.Empty);
    }

    [TestCase("int IVersion.__Version { get => 0; set { } }")]
    [TestCase("void IVersion.__Reset() { }")]
    [TestCase("int IVersionSync.__SyncId { get => 0; set { } }")]
    [TestCase("SyncContext IVersionSync.__SyncContext { get => null; set { } }")]
    [TestCase("bool IVersionSync.__IsDirty => false;")]
    [TestCase("int IVersion.Version => 0;")]
    [TestCase("void IVersion.Reset() { }")]
    [TestCase("int IVersionSync.SyncId => 0;")]
    [TestCase("SyncContext IVersionSync.SyncContext => null;")]
    [TestCase("bool IVersionSync.IsDirty => false;")]
    public void ExplicitVersionInterfaceMember_IsReserved(string member)
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator($@"
namespace Test
{{
    public partial class Data : IVersionSync
    {{
        [VersionField] private int __Value;
        {member}
    }}
}}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10004");
        Assert.That(result.GeneratedSources, Is.Empty);
    }

    [Test]
    public void VersionFieldContainerCodecsAndFactories_AreEmittedOncePerOwnerType()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class Child : IVersionSync
    {
        [VersionField] private int __Value;
    }

    public partial class Owner : IVersionSync
    {
        [VersionField] private VersionSyncList<int> __First;
        [VersionField] private VersionSyncList<int> __Second;
        [VersionField] private VersionSyncDictionary<int, int> __Lookup;
        [VersionField] private VersionSyncList<Child> __Children;
        [VersionField] private VersionSyncHashSet<Child> __UniqueChildren;
        [VersionField] private VersionSyncDictionary<int, Child> __ChildLookup;
    }
}");

        GeneratorTestHelper.AssertNoErrors(result);
        string generated = GeneratorTestHelper.GetGeneratedForClass(result, "Owner")!;
        Assert.That(generated.Split(new[] { "private static void __wScalar_" }, StringSplitOptions.None).Length - 1,
            Is.EqualTo(1));
        Assert.That(generated.Split(new[] { "private static int __rScalar_" }, StringSplitOptions.None).Length - 1,
            Is.EqualTo(1));
        Assert.That(generated.Split(new[] { "private static ReactiveBinding.IVersionSync __newSync_" }, StringSplitOptions.None).Length - 1,
            Is.EqualTo(1));
    }

    [Test]
    public void VersionFieldGeneratedPropertyCannotConflictWithSyncApi()
    {
        var result = GeneratorTestHelper.RunVersionFieldGenerator(@"
namespace Test
{
    public partial class Data : IVersionSync
    {
        [VersionField] private int __attachTo;
    }
}");

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF10007");
        Assert.That(result.GeneratedSources, Is.Empty);
    }

    private static void AssertNoCompilationErrors(GeneratorRunResult result)
    {
        var errors = result.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors.Select(e => $"{e.Id}: {e.GetMessage()}")));
    }

    private static byte[] Full(SyncContext context)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        context.CaptureFull(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Delta(SyncContext context)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        context.CaptureDelta(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Apply(SyncContext context, byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);
        context.Apply(reader);
    }
}
