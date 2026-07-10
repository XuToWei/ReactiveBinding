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
        public int __Version => 0;
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
        Full(consumerContext); // clear AttachTo's initial dirty state before receiving remote data

        producer.Value = 12;
        producer.Items.Add(3);
        Apply(consumerContext, Full(producerContext));

        Assert.That(Delta(consumerContext), Is.EqualTo(new byte[] { 0 }));
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30007");
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30008");
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30011");
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB30011");
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB20006");
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF20004");
    }

    [Test]
    public void VersionFieldPropertyCollision_WithMethodIsRejected()
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF20003");
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF20003");
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF20005");
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
    public async System.Threading.Tasks.Task ConditionalParentAccess_IsRejected()
    {
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(@"
namespace Test
{
    public class Consumer
    {
        public object Read(IVersion value) => value?.__Parent;
    }
}");

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(d => d.Id == "VF30001"));
    }

    [Test]
    public async System.Threading.Tasks.Task UserClassNamedVersionList_DoesNotBypassParentAnalyzer()
    {
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(@"
namespace Test
{
    public class VersionList
    {
        public void Write(IVersion value) { value.__Parent = null; }
    }
}");

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(d => d.Id == "VF30001"));
    }

    [Test]
    public async System.Threading.Tasks.Task UnrelatedVersionOwnershipName_DoesNotBypassParentAnalyzer()
    {
        var diagnostics = await GeneratorTestHelper.RunParentAccessAnalyzer(@"
namespace Other
{
    public class VersionOwnership
    {
        public void Write(IVersion value) { value.__Parent = null; }
    }
}");

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(d => d.Id == "VF30001"));
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
        public int __Version => 0;
        public IVersion __Parent { get; set; }
        public void __IncrementVersion() { }
        public void __Reset() { }
    }
}");

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(d => d.Id == "VF30002"));
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

        var diagnostic = diagnostics.Single(d => d.Id == "RB0003");
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

        Assert.That(diagnostics, Has.Some.Matches<Diagnostic>(d => d.Id == "RB0003"));
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "RB20004");
    }

    [Test]
    public void StandaloneReactiveRoot_IsVirtualForCrossAssemblyInheritance()
    {
        var result = GeneratorTestHelper.RunGenerator(@"
namespace Test
{
    public partial class BaseObserver : IReactiveObserver
    {
        [ReactiveSource] private int Value;
        [ReactiveBind(nameof(Value))] private void Changed() { }
    }
}");

        GeneratorTestHelper.AssertGeneratedContains(result, "public virtual void ObserveChanges()");
        GeneratorTestHelper.AssertGeneratedContains(result, "public virtual void ResetChanges()");
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

        GeneratorTestHelper.AssertHasDiagnostic(result, "VF20003");
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
