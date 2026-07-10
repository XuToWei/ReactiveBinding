using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ReactiveBinding;

namespace ReactiveBinding.SourceGenerator.Tests;

[TestFixture]
public class RuntimeRegressionTests
{
    [Test]
    public void VersionList_RejectsChildThatAlreadyHasAnOwner()
    {
        var child = new TestVersion(1);
        var first = new VersionList<TestVersion> { child };
        var second = new VersionList<TestVersion>();

        var ex = Assert.Throws<InvalidOperationException>(() => second.Add(child));

        Assert.That(ex!.Message, Does.Contain("already"));
        Assert.That(second, Is.Empty);
        Assert.That(child.__Parent, Is.SameAs(first));
        Assert.Throws<InvalidOperationException>(() => first.Add(child));
        Assert.That(first.Count, Is.EqualTo(1));
    }

    [Test]
    public void VersionDictionary_RejectsSameChildInTwoSlots()
    {
        var child = new TestVersion(1);
        var dictionary = new VersionDictionary<string, TestVersion>
        {
            ["first"] = child
        };

        Assert.Throws<InvalidOperationException>(() => dictionary["second"] = child);

        Assert.That(dictionary.Count, Is.EqualTo(1));
        Assert.That(child.__Parent, Is.SameAs(dictionary));
    }

    [Test]
    public void Reset_KeepsInternalParentChainForReuse()
    {
        var child = new TestVersion(1);
        var list = new VersionList<TestVersion> { child };

        list.__Reset();

        Assert.That(child.__Parent, Is.SameAs(list));
        child.__IncrementVersion();
        Assert.That(list.__Version, Is.GreaterThan(0));
    }

    [Test]
    public void RemoveRange_InvalidRangeDoesNotMutateOwnership()
    {
        var first = new TestVersion(1);
        var second = new TestVersion(2);
        var list = new VersionList<TestVersion> { first, second };

        Assert.Throws<ArgumentException>(() => list.RemoveRange(1, 2));

        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(first.__Parent, Is.SameAs(list));
        Assert.That(second.__Parent, Is.SameAs(list));
    }

    [Test]
    public void VersionSyncList_RemoveRangeFailureKeepsRegistryAndOwnership()
    {
        var first = new TestSyncVersion(1);
        var second = new TestSyncVersion(2);
        var list = new VersionSyncList<TestSyncVersion> { first, second };
        list.__InitSync(() => new TestSyncVersion(0));
        var context = new SyncContext();
        list.AttachTo(context);
        var firstId = first.__SyncId;
        var secondId = second.__SyncId;

        Assert.Throws<ArgumentException>(() => list.RemoveRange(1, 2));

        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(first.__Parent, Is.SameAs(list));
        Assert.That(second.__Parent, Is.SameAs(list));
        Assert.That(context.__Objects[firstId], Is.SameAs(first));
        Assert.That(context.__Objects[secondId], Is.SameAs(second));
    }

    [Test]
    public void Remove_DetachesActualStoredInstance_NotEqualProbe()
    {
        var stored = new TestVersion(1);
        var equalProbe = new TestVersion(1);
        var list = new VersionList<TestVersion> { stored };

        Assert.That(list.Remove(equalProbe), Is.True);

        Assert.That(stored.__Parent, Is.Null);
        Assert.That(equalProbe.__Parent, Is.Null);
    }

    [Test]
    public void HashSetBulkOperationsUseTheSetComparer()
    {
        var intersect = new VersionHashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha" };
        intersect.IntersectWith(new[] { "ALPHA" });

        var symmetric = new VersionHashSet<string>(StringComparer.OrdinalIgnoreCase);
        symmetric.SymmetricExceptWith(new[] { "alpha", "ALPHA" });

        Assert.That(intersect, Is.EquivalentTo(new[] { "alpha" }));
        Assert.That(symmetric, Is.EquivalentTo(new[] { "alpha" }));
    }

    [Test]
    public void SyncContainersRejectCustomComparersImmediately()
    {
        Assert.Throws<NotSupportedException>(() =>
            new VersionSyncDictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        Assert.Throws<NotSupportedException>(() =>
            new VersionSyncHashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    [Test]
    public void AttachToSecondContextIsRejected()
    {
        var list = new VersionSyncList<int>();
        var first = new SyncContext();
        var second = new SyncContext();
        list.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        list.AttachTo(first);

        Assert.Throws<InvalidOperationException>(() => list.AttachTo(second));

        Assert.That(list.__SyncContext, Is.SameAs(first));
        Assert.That(first.__Objects[list.__SyncId], Is.SameAs(list));
        Assert.That(second.__Objects, Is.Empty);
    }

    private class TestVersion : IVersion, IEquatable<TestVersion>
    {
        public TestVersion(int id) => Id = id;

        public int Id { get; }
        public int __Version { get; private set; }
        public IVersion __Parent { get; set; } = null!;

        public void __IncrementVersion()
        {
            __Version = VersionCounter.Next();
            __Parent?.__IncrementVersion();
        }

        public virtual void __Reset()
        {
            __Version = 0;
            __Parent = null!;
        }

        public bool Equals(TestVersion? other) => other?.Id == Id;
        public override bool Equals(object? obj) => obj is TestVersion other && Equals(other);
        public override int GetHashCode() => Id;
    }

    private sealed class TestSyncVersion : TestVersion, IVersionSync
    {
        public TestSyncVersion(int id) : base(id) { }

        public int __SyncId { get; set; }
        public SyncContext __SyncContext { get; set; } = null!;
        public bool __IsDirty { get; private set; }

        public void AttachTo(SyncContext ctx)
        {
            if (__SyncContext != null && !ReferenceEquals(__SyncContext, ctx))
                throw new InvalidOperationException();
            __SyncContext = ctx;
            if (__SyncId == 0) __SyncId = ctx.__NextId++;
            ctx.__Objects[__SyncId] = this;
        }

        public void __CaptureFull(BinaryWriter writer) => writer.Write(__SyncId);
        public void __CaptureDelta(BinaryWriter writer) => writer.Write(__SyncId);
        public void __ClearDirty() => __IsDirty = false;
        public void __MarkAllDirty() => __IsDirty = true;
        public void __Apply(BinaryReader reader) { }
        public void __SyncChildren(SyncOp op) { }

        public override void __Reset()
        {
            if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);
            __SyncId = 0;
            __SyncContext = null!;
            __IsDirty = false;
            base.__Reset();
        }
    }
}
