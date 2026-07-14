#nullable disable
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
    public void EnsureCanAttachAll_ReusesAndClearsThreadLocalScratch()
    {
        var parent = new TestVersion(1);
        var first = new TestVersion(2);
        var second = new TestVersion(3);
        var scratchField = typeof(VersionOwnership).GetField(
            "s_AttachSeenScratch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.That(scratchField, Is.Not.Null);
        VersionOwnership.EnsureCanAttachAll(parent, new[] { first, second });
        var firstScratch = (HashSet<IVersion>)scratchField.GetValue(null);

        Assert.That(firstScratch, Is.Not.Null);
        Assert.That(firstScratch, Is.Empty);

        VersionOwnership.EnsureCanAttachAll(parent, new[] { first, second });
        var secondScratch = (HashSet<IVersion>)scratchField.GetValue(null);

        Assert.That(secondScratch, Is.SameAs(firstScratch));
        Assert.That(secondScratch, Is.Empty);

        Assert.Throws<InvalidOperationException>(() =>
            VersionOwnership.EnsureCanAttachAll(parent, new[] { first, first }));
        Assert.That((HashSet<IVersion>)scratchField.GetValue(null), Is.Empty);
        Assert.DoesNotThrow(() => VersionOwnership.EnsureCanAttachAll(parent, new[] { first, second }));
    }

    [Test]
    public void EnsureCanAttachAll_ReentrantEnumerationKeepsOuterDuplicateTracking()
    {
        var parent = new TestVersion(1);
        var repeated = new TestVersion(2);
        var otherOuterChild = new TestVersion(3);
        var nestedParent = new TestVersion(4);
        var nestedChild = new TestVersion(5);
        var secondNestedChild = new TestVersion(6);

        IEnumerable<TestVersion> ReentrantChildren()
        {
            yield return repeated;
            yield return otherOuterChild;
            VersionOwnership.EnsureCanAttachAll(nestedParent, new[] { nestedChild, secondNestedChild });
            yield return repeated;
        }

        Assert.Throws<InvalidOperationException>(() =>
            VersionOwnership.EnsureCanAttachAll(parent, ReentrantChildren()));
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
    public void HashSetBulkOperationsUseDefaultEqualityAndDetachStoredInstances()
    {
        var intersectItem = new TestVersion(1);
        var equalIntersectProbe = new TestVersion(1);
        var intersect = new VersionHashSet<TestVersion> { intersectItem };
        intersect.IntersectWith(new[] { equalIntersectProbe });

        var symmetricItem = new TestVersion(2);
        var equalSymmetricItem = new TestVersion(2);
        var symmetric = new VersionHashSet<TestVersion> { symmetricItem };
        symmetric.SymmetricExceptWith(new[] { equalSymmetricItem });

        TestVersion retained = null;
        foreach (var item in intersect) retained = item;
        Assert.That(intersect.Count, Is.EqualTo(1));
        Assert.That(retained, Is.SameAs(intersectItem));
        Assert.That(intersectItem.__Parent, Is.SameAs(intersect));
        Assert.That(equalIntersectProbe.__Parent, Is.Null);
        Assert.That(symmetric, Is.Empty);
        Assert.That(symmetricItem.__Parent, Is.Null);
        Assert.That(equalSymmetricItem.__Parent, Is.Null);
    }

    [Test]
    public void HashSetsDoNotExposeCustomComparerConstructors()
    {
        foreach (var type in new[] { typeof(VersionHashSet<string>), typeof(VersionSyncHashSet<string>) })
        foreach (var constructor in type.GetConstructors())
        foreach (var parameter in constructor.GetParameters())
        {
            Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(IEqualityComparer<string>)),
                $"{type.Name} still exposes a custom comparer constructor.");
        }
    }

    [TestCase(typeof(VersionList<int>))]
    [TestCase(typeof(VersionDictionary<string, int>))]
    [TestCase(typeof(VersionHashSet<int>))]
    [TestCase(typeof(VersionSyncList<int>))]
    [TestCase(typeof(VersionSyncDictionary<string, int>))]
    [TestCase(typeof(VersionSyncHashSet<int>))]
    public void VersionContainersStoreVersionInPublicProtocolProperty(Type containerType)
    {
        const System.Reflection.BindingFlags instanceMembers =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        Assert.That(containerType.GetField("m_Version", instanceMembers), Is.Null);

        var property = containerType.GetProperty("__Version", instanceMembers);
        Assert.That(property, Is.Not.Null);
        Assert.That(property.GetMethod, Is.Not.Null.And.Property("IsPublic").True);
        Assert.That(property.SetMethod, Is.Not.Null.And.Property("IsPublic").True);

        var container = (IVersion)Activator.CreateInstance(containerType);
        Assert.That(container.Version, Is.Zero);
        container.__IncrementVersion();
        Assert.That(container.Version, Is.GreaterThan(0));
        container.Reset();
        Assert.That(container.Version, Is.Zero);
    }

    [Test]
    public void VersionSyncDictionaryRejectsCustomComparerImmediately()
    {
        Assert.Throws<NotSupportedException>(() =>
            new VersionSyncDictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    }

    [Test]
    public void AttachToSecondContextIsRejected()
    {
        var list = new VersionSyncList<int>();
        var first = new SyncContext();
        var second = new SyncContext();
        list.InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        list.AttachTo(first);

        Assert.Throws<InvalidOperationException>(() => list.AttachTo(second));

        IVersionSync syncList = list;
        Assert.That(syncList.SyncContext, Is.SameAs(first));
        Assert.That(first.__Objects[syncList.SyncId], Is.SameAs(list));
        Assert.That(second.__Objects, Is.Empty);
    }

    [Test]
    public void VersionHashSet_ElementCanBeRemovedBeforeMutatingHashFields()
    {
        var item = new TestVersion(1);
        var set = new VersionHashSet<TestVersion> { item };

        Assert.That(set.Remove(item), Is.True);
        Assert.That(item.__Parent, Is.Null);
        item.Id = 2;

        Assert.That(set.Add(item), Is.True);
        Assert.That(set.Contains(item), Is.True);
        Assert.That(item.__Parent, Is.SameAs(set));
    }

    [Test]
    public void VersionHashSet_DistinctEqualVersionObjectsUseDefaultEquality()
    {
        var first = new TestVersion(1);
        var second = new TestVersion(1);
        var set = new VersionHashSet<TestVersion>();

        Assert.That(set.Add(first), Is.True);
        Assert.That(set.Add(second), Is.False);

        Assert.That(set.Comparer, Is.SameAs(EqualityComparer<TestVersion>.Default));
        Assert.That(set.Count, Is.EqualTo(1));
        Assert.That(set.Contains(second), Is.True);
        Assert.That(first.__Parent, Is.SameAs(set));
        Assert.That(second.__Parent, Is.Null);
    }

    [Test]
    public void VersionHashSet_RemoveEqualProbeDetachesStoredInstance()
    {
        var stored = new TestVersion(1);
        var equalProbe = new TestVersion(1);
        var set = new VersionHashSet<TestVersion> { stored };

        Assert.That(set.Remove(equalProbe), Is.True);

        Assert.That(set, Is.Empty);
        Assert.That(stored.__Parent, Is.Null);
        Assert.That(equalProbe.__Parent, Is.Null);
    }

    [Test]
    public void VersionSyncHashSet_ElementCanBeRemovedBeforeMutatingHashFields()
    {
        var item = new TestSyncVersion(1);
        var set = new VersionSyncHashSet<TestSyncVersion> { item };
        set.__InitSync(() => new TestSyncVersion(0));
        var context = new SyncContext();
        set.AttachTo(context);
        int itemId = item.__SyncId;

        Assert.That(set.Remove(item), Is.True);
        Assert.That(item.__Parent, Is.Null);
        Assert.That(item.__SyncId, Is.Zero);
        Assert.That(context.__Objects.ContainsKey(itemId), Is.False);
        item.Id = 2;

        Assert.That(set.Add(item), Is.True);
        Assert.That(set.Contains(item), Is.True);
        Assert.That(item.__Parent, Is.SameAs(set));
        Assert.That(item.__SyncId, Is.GreaterThan(0));
        Assert.That(context.__Objects[item.__SyncId], Is.SameAs(item));
    }

    [Test]
    public void VersionSyncHashSet_RemoveEqualProbeDetachesAndUnregistersStoredInstance()
    {
        var stored = new TestSyncVersion(1);
        var equalProbe = new TestSyncVersion(1);
        var set = new VersionSyncHashSet<TestSyncVersion> { stored };
        set.__InitSync(() => new TestSyncVersion(0));
        var context = new SyncContext();
        set.AttachTo(context);
        int storedId = stored.__SyncId;

        Assert.That(set.Remove(equalProbe), Is.True);

        Assert.That(set, Is.Empty);
        Assert.That(stored.__Parent, Is.Null);
        Assert.That(stored.__SyncId, Is.Zero);
        Assert.That(context.__Objects.ContainsKey(storedId), Is.False);
        Assert.That(equalProbe.__Parent, Is.Null);
        Assert.That(equalProbe.__SyncId, Is.Zero);
    }

    [Test]
    public void SyncContainers_AttachWithoutSerializerInitialization_IsRejectedWithoutMutatingContext()
    {
        AssertUninitializedAttachIsRejected(new VersionSyncList<int>());
        AssertUninitializedAttachIsRejected(new VersionSyncDictionary<string, int>());
        AssertUninitializedAttachIsRejected(new VersionSyncHashSet<int>());
    }

    [TestCase("List", true)]
    [TestCase("List", false)]
    [TestCase("Dictionary", true)]
    [TestCase("Dictionary", false)]
    [TestCase("HashSet", true)]
    [TestCase("HashSet", false)]
    public void SyncContainers_ApplyRejectsNegativeFullAndOperationCounts(string kind, bool full)
    {
        var container = CreateInitializedScalarContainer(kind);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(full ? (byte)1 : (byte)0);
            writer.WriteVarInt32(-1);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.Throws<InvalidDataException>(() => container.__Apply(reader));
    }

    [Test]
    public void VersionSyncList_RepeatedSetAtSameIndexCoalescesToOneDeltaOp()
    {
        var producer = new VersionSyncList<int>();
        for (int i = 0; i < 100; i++) producer.Add(i);
        producer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var producerContext = new SyncContext();
        producer.AttachTo(producerContext);

        var consumer = new VersionSyncList<int>();
        consumer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var consumerContext = new SyncContext();
        consumer.AttachTo(consumerContext);
        Apply(consumerContext, CaptureFull(producerContext));

        for (int i = 1; i <= 40; i++) producer[50] = 1000 + i;
        var delta = CaptureDelta(producerContext);

        Assert.That(ReadFirstContainerRecordMode(delta), Is.Zero, "repeated SETs should retain only the final delta op");
        Apply(consumerContext, delta);
        Assert.That(consumer[50], Is.EqualTo(1040));
    }

    [Test]
    public void VersionSyncDictionary_RepeatedSetForSameKeyCoalescesToOneDeltaOp()
    {
        var producer = new VersionSyncDictionary<string, int>();
        for (int i = 0; i < 100; i++) producer["key-" + i] = i;
        producer.__InitSync(
            (writer, value) => writer.Write(value), reader => reader.ReadString(),
            (writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var producerContext = new SyncContext();
        producer.AttachTo(producerContext);

        var consumer = new VersionSyncDictionary<string, int>();
        consumer.__InitSync(
            (writer, value) => writer.Write(value), reader => reader.ReadString(),
            (writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var consumerContext = new SyncContext();
        consumer.AttachTo(consumerContext);
        Apply(consumerContext, CaptureFull(producerContext));

        for (int i = 1; i <= 40; i++) producer["key-50"] = 1000 + i;
        var delta = CaptureDelta(producerContext);

        Assert.That(ReadFirstContainerRecordMode(delta), Is.Zero, "repeated SETs should retain only the final delta op");
        Apply(consumerContext, delta);
        Assert.That(consumer["key-50"], Is.EqualTo(1040));
    }

    [Test]
    public void VersionSyncList_RangeOpcodesUseVarInt32IndexesAndRoundTrip()
    {
        var producer = new VersionSyncList<int>();
        for (int i = 0; i < 200; i++) producer.Add(i);
        producer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var producerContext = new SyncContext();
        producer.AttachTo(producerContext);

        var consumer = new VersionSyncList<int>();
        consumer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var consumerContext = new SyncContext();
        consumer.AttachTo(consumerContext);
        Apply(consumerContext, CaptureFull(producerContext));

        producer.AddRange(new[] { 200, 201, 202 });
        producer.InsertRange(130, new[] { -2, -1 });
        producer.RemoveRange(150, 3);
        var delta = CaptureDelta(producerContext);

        using (var stream = new MemoryStream(delta))
        using (var reader = new BinaryReader(stream))
        {
            Assert.That(reader.ReadByte(), Is.Zero);
            Assert.That(reader.ReadVarInt32(), Is.EqualTo(producer.__SyncId));
            Assert.That(reader.ReadByte(), Is.Zero, "range changes should remain an op-log delta");
            Assert.That(reader.ReadVarInt32(), Is.EqualTo(3));

            Assert.That(reader.ReadByte(), Is.EqualTo(6), "AddRange opcode");
            Assert.That(reader.ReadVarInt32(), Is.EqualTo(3));
            Assert.That(new[] { reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() },
                Is.EqualTo(new[] { 200, 201, 202 }));

            Assert.That(reader.ReadByte(), Is.EqualTo(7), "InsertRange opcode");
            Assert.That(reader.ReadVarInt32(), Is.EqualTo(130));
            Assert.That(reader.ReadVarInt32(), Is.EqualTo(2));
            Assert.That(new[] { reader.ReadInt32(), reader.ReadInt32() }, Is.EqualTo(new[] { -2, -1 }));

            Assert.That(reader.ReadByte(), Is.EqualTo(8), "RemoveRange opcode");
            Assert.That(reader.ReadVarInt32(), Is.EqualTo(150));
            Assert.That(reader.ReadVarInt32(), Is.EqualTo(3));
            Assert.That(reader.ReadVarInt32(), Is.Zero);
            Assert.That(reader.ReadVarInt32(), Is.Zero);
        }

        Apply(consumerContext, delta);
        Assert.That(consumer, Is.EqualTo(producer));
    }

    [Test]
    public void VersionSyncList_SetsSeparatedByMovementReplayInOrderWithoutUnsafeMerge()
    {
        var producer = new VersionSyncList<int>();
        for (int i = 0; i < 10; i++) producer.Add(i);
        producer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var producerContext = new SyncContext();
        producer.AttachTo(producerContext);

        var consumer = new VersionSyncList<int>();
        consumer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var consumerContext = new SyncContext();
        consumer.AttachTo(consumerContext);
        Apply(consumerContext, CaptureFull(producerContext));

        producer[5] = 50;
        producer.RemoveAt(0);       // the previously written element moves from index 5 to index 4
        producer[4] = 500;

        var delta = CaptureDelta(producerContext);
        Assert.That(ReadFirstContainerRecordMode(delta), Is.Zero);
        Apply(consumerContext, delta);
        Assert.That(consumer, Is.EqualTo(producer));
        Assert.That(consumer[4], Is.EqualTo(500));
    }

    [Test]
    public void SmallSingleOperationChoosesSmallerFullRecordForEverySyncContainer()
    {
        var list = new VersionSyncList<int> { 0 };
        list.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var listContext = new SyncContext();
        list.AttachTo(listContext);
        CaptureFull(listContext);
        list[0] = 1;

        var dictionary = new VersionSyncDictionary<int, int> { [1] = 0 };
        dictionary.__InitSync(
            (writer, value) => writer.Write(value), reader => reader.ReadInt32(),
            (writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var dictionaryContext = new SyncContext();
        dictionary.AttachTo(dictionaryContext);
        CaptureFull(dictionaryContext);
        dictionary[1] = 1;

        var set = new VersionSyncHashSet<int>();
        set.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var setContext = new SyncContext();
        set.AttachTo(setContext);
        CaptureFull(setContext);
        set.Add(1);

        Assert.That(ReadFirstContainerRecordMode(CaptureDelta(listContext)), Is.EqualTo(1));
        Assert.That(ReadFirstContainerRecordMode(CaptureDelta(dictionaryContext)), Is.EqualTo(1));
        Assert.That(ReadFirstContainerRecordMode(CaptureDelta(setContext)), Is.EqualTo(1));
    }

    [Test]
    public void VersionSyncHashSet_ObjectBulkDifferenceUsesDeltaWithLargeVarInt32IdsAndTombstone()
    {
        var producer = new VersionSyncHashSet<ReferenceSyncVersion>();
        var sourceItems = new List<ReferenceSyncVersion>();
        for (int i = 0; i < 140; i++)
        {
            var item = new ReferenceSyncVersion(i);
            sourceItems.Add(item);
            producer.Add(item);
        }
        producer.__InitSync(() => new ReferenceSyncVersion(0));
        var producerContext = new SyncContext();
        producer.AttachTo(producerContext);

        var consumer = new VersionSyncHashSet<ReferenceSyncVersion>();
        consumer.__InitSync(() => new ReferenceSyncVersion(0));
        var consumerContext = new SyncContext();
        consumer.AttachTo(consumerContext);
        Apply(consumerContext, CaptureFull(producerContext));

        var removed = sourceItems[130];
        int removedId = removed.__SyncId;
        var staleConsumerNode = consumerContext.__Objects[removedId];
        producer.ExceptWith(new[] { removed });
        var added = new ReferenceSyncVersion(999);
        producer.UnionWith(new[] { added });

        var delta = CaptureDelta(producerContext);
        Assert.That(removedId, Is.GreaterThan(127));
        Assert.That(added.__SyncId, Is.GreaterThan(127));
        Assert.That(ReadFirstContainerRecordMode(delta), Is.Zero, "two changes in a large set should use add/remove differences");

        Apply(consumerContext, delta);
        Assert.That(consumer.Count, Is.EqualTo(140));
        Assert.That(consumerContext.__Objects.ContainsKey(removedId), Is.False);
        Assert.That(staleConsumerNode.__SyncId, Is.Zero);
        Assert.That(consumerContext.__Objects.ContainsKey(added.__SyncId), Is.True);
        Assert.That(consumer.Contains((ReferenceSyncVersion)consumerContext.__Objects[added.__SyncId]), Is.True);
    }

    [Test]
    public void SortIfNeededSkipsAlreadyOrderedListsAndSynchronizesActualReorder()
    {
        var tracked = new VersionList<int>(new[] { 1, 2, 3 });
        int orderedVersion = tracked.__Version;
        Assert.That(tracked.SortIfNeeded(), Is.False);
        Assert.That(tracked.__Version, Is.EqualTo(orderedVersion));

        var producer = new VersionSyncList<int> { 1, 2, 3 };
        producer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var producerContext = new SyncContext();
        producer.AttachTo(producerContext);
        CaptureFull(producerContext);
        int syncOrderedVersion = producer.__Version;
        Assert.That(producer.SortIfNeeded(), Is.False);
        Assert.That(producer.__Version, Is.EqualTo(syncOrderedVersion));
        Assert.That(CaptureDelta(producerContext), Is.EqualTo(new byte[] { 0, 0, 0 }));

        producer.Reverse();
        CaptureFull(producerContext);
        var consumer = new VersionSyncList<int>();
        consumer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var consumerContext = new SyncContext();
        consumer.AttachTo(consumerContext);
        Apply(consumerContext, CaptureFull(producerContext));

        Assert.That(producer.SortIfNeeded(), Is.True);
        Apply(consumerContext, CaptureDelta(producerContext));
        Assert.That(consumer, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void VersionSyncHashSet_AddThenRemoveInSameFrameCancelsDeltaOp()
    {
        var producer = new VersionSyncHashSet<int> { 999 };
        producer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var producerContext = new SyncContext();
        producer.AttachTo(producerContext);

        var consumer = new VersionSyncHashSet<int>();
        consumer.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
        var consumerContext = new SyncContext();
        consumer.AttachTo(consumerContext);
        Apply(consumerContext, CaptureFull(producerContext));

        for (int i = 0; i < 20; i++)
        {
            producer.Add(i);
            producer.Remove(i);
        }
        var delta = CaptureDelta(producerContext);

        Assert.That(ReadFirstNodeId(delta), Is.Zero, "inverse operations should leave no dirty node record");
        Apply(consumerContext, delta);
        Assert.That(consumer, Is.EquivalentTo(new[] { 999 }));
    }

    [Test]
    public void SyncContext_SparseRegistryStillCapturesActiveNodesInAscendingIdOrder()
    {
        var context = new SyncContext { __NextId = 1_000_001 };
        var high = new TestSyncVersion(100) { __SyncId = 100, __SyncContext = context };
        var low = new TestSyncVersion(2) { __SyncId = 2, __SyncContext = context };
        context.__Objects[high.__SyncId] = high;
        context.__Objects[low.__SyncId] = low;

        var frame = CaptureFull(context);

        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadVarInt32(), Is.EqualTo(2));
        Assert.That(reader.ReadVarInt32(), Is.EqualTo(100));
        Assert.That(reader.ReadVarInt32(), Is.Zero, "expected the node-record terminator");
        Assert.That(reader.ReadVarInt32(), Is.Zero, "full frames have no tombstones");
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [Test]
    public void VersionSyncDictionary_ResetRetainsValuesAndRebuildsOwnershipForReuse()
    {
        var child = new TestSyncVersion(1);
        var dictionary = new VersionSyncDictionary<string, TestSyncVersion> { ["child"] = child };
        dictionary.__InitSync(
            (writer, value) => writer.Write(value), reader => reader.ReadString(),
            () => new TestSyncVersion(0));
        var firstContext = new SyncContext();
        dictionary.AttachTo(firstContext);

        dictionary.__Reset();

        Assert.That(dictionary["child"], Is.SameAs(child));
        Assert.That(child.__Parent, Is.SameAs(dictionary));
        Assert.That(dictionary.__SyncId, Is.Zero);
        Assert.That(child.__SyncId, Is.Zero);
        Assert.That(dictionary.__SyncContext, Is.Null);
        Assert.That(child.__SyncContext, Is.Null);
        Assert.That(firstContext.__Objects, Is.Empty);

        var secondContext = new SyncContext();
        dictionary.AttachTo(secondContext);
        Assert.That(dictionary.__SyncId, Is.GreaterThan(0));
        Assert.That(child.__SyncId, Is.GreaterThan(0));
    }

    [Test]
    public void VersionSyncHashSet_ResetRetainsValuesAndRebuildsOwnershipForReuse()
    {
        var child = new TestSyncVersion(1);
        var set = new VersionSyncHashSet<TestSyncVersion> { child };
        set.__InitSync(() => new TestSyncVersion(0));
        var firstContext = new SyncContext();
        set.AttachTo(firstContext);

        set.__Reset();

        Assert.That(set.Single(), Is.SameAs(child));
        Assert.That(child.__Parent, Is.SameAs(set));
        Assert.That(set.__SyncId, Is.Zero);
        Assert.That(child.__SyncId, Is.Zero);
        Assert.That(set.__SyncContext, Is.Null);
        Assert.That(child.__SyncContext, Is.Null);
        Assert.That(firstContext.__Objects, Is.Empty);

        var secondContext = new SyncContext();
        set.AttachTo(secondContext);
        Assert.That(set.__SyncId, Is.GreaterThan(0));
        Assert.That(child.__SyncId, Is.GreaterThan(0));
    }

    [Test]
    public void VersionSyncHashSet_DistinctEqualVersionObjectsUseDefaultEquality()
    {
        var first = new TestSyncVersion(1);
        var second = new TestSyncVersion(1);
        var set = new VersionSyncHashSet<TestSyncVersion>();

        Assert.That(set.Add(first), Is.True);
        Assert.That(set.Add(second), Is.False);

        Assert.That(set.Comparer, Is.SameAs(EqualityComparer<TestSyncVersion>.Default));
        Assert.That(set.Count, Is.EqualTo(1));
        Assert.That(set.Contains(second), Is.True);
        Assert.That(first.__Parent, Is.SameAs(set));
        Assert.That(second.__Parent, Is.Null);
    }

    private static void AssertUninitializedAttachIsRejected(IVersionSync container)
    {
        var context = new SyncContext();

        var ex = Assert.Throws<InvalidOperationException>(() => container.AttachTo(context));

        Assert.That(ex!.Message, Does.Contain("initialized"));
        Assert.That(context.__Objects, Is.Empty);
        Assert.That(container.__SyncId, Is.Zero);
        Assert.That(container.__SyncContext, Is.Null);
    }

    private static IVersionSync CreateInitializedScalarContainer(string kind)
    {
        IVersionSync container;
        switch (kind)
        {
            case "List":
            {
                var list = new VersionSyncList<int>();
                list.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
                container = list;
                break;
            }
            case "Dictionary":
            {
                var dictionary = new VersionSyncDictionary<int, int>();
                dictionary.__InitSync(
                    (writer, value) => writer.Write(value), reader => reader.ReadInt32(),
                    (writer, value) => writer.Write(value), reader => reader.ReadInt32());
                container = dictionary;
                break;
            }
            case "HashSet":
            {
                var set = new VersionSyncHashSet<int>();
                set.__InitSync((writer, value) => writer.Write(value), reader => reader.ReadInt32());
                container = set;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        container.AttachTo(new SyncContext());
        return container;
    }

    private static byte[] CaptureFull(SyncContext context)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        context.CaptureFull(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CaptureDelta(SyncContext context)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        context.CaptureDelta(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Apply(SyncContext context, byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        context.Apply(reader);
    }

    private static byte ReadFirstContainerRecordMode(byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        Assert.That(reader.ReadByte(), Is.Zero, "expected a delta frame");
        Assert.That(reader.ReadVarInt32(), Is.GreaterThan(0), "expected a container node record");
        return reader.ReadByte();
    }

    private static int ReadFirstNodeId(byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        Assert.That(reader.ReadByte(), Is.Zero, "expected a delta frame");
        return reader.ReadVarInt32();
    }

    private class TestVersion : IVersion, IEquatable<TestVersion>
    {
        public TestVersion(int id) => Id = id;

        public int Id { get; set; }
        public int Version => __Version;
        public int __Version { get; set; }
        public IVersion __Parent { get; set; }

        public void Reset() => __Reset();

        public void __IncrementVersion()
        {
            __Version = VersionCounter.Next();
            __Parent?.__IncrementVersion();
        }

        public virtual void __Reset()
        {
            __Version = 0;
            __Parent = null;
        }

        public bool Equals(TestVersion other) => other?.Id == Id;
        public override bool Equals(object obj) => obj is TestVersion other && Equals(other);
        public override int GetHashCode() => Id;
    }

    private class TestSyncVersion : TestVersion, IVersionSync
    {
        public TestSyncVersion(int id) : base(id) { }

        public int __SyncId { get; set; }
        public SyncContext __SyncContext { get; set; }
        public bool __IsDirty { get; private set; }

        public void AttachTo(SyncContext ctx)
        {
            if (__SyncContext != null && !ReferenceEquals(__SyncContext, ctx))
                throw new InvalidOperationException();
            __SyncContext = ctx;
            if (__SyncId == 0) __SyncId = ctx.__AllocateId();
            ctx.__Objects[__SyncId] = this;
        }

        public void __CaptureFull(BinaryWriter writer) => writer.WriteVarInt32(__SyncId);
        public void __CaptureDelta(BinaryWriter writer) => writer.WriteVarInt32(__SyncId);
        public void __ClearDirty() => __IsDirty = false;
        public void __MarkAllDirty()
        {
            __IsDirty = true;
            // A node may already be dirty before it is attached, when it has no context in which to enlist.
            // The context set deduplicates an already-enlisted id.
            if (__SyncContext != null) __SyncContext.__EnlistDirty(__SyncId);
        }
        public void __Apply(BinaryReader reader) => __SyncContext?.__TouchVersion(this);
        public void __SyncChildren(SyncOp op) { }

        public override void __Reset()
        {
            if (__SyncContext != null) __SyncContext.__Objects.Remove(__SyncId);
            __SyncId = 0;
            __SyncContext = null;
            __IsDirty = false;
            base.__Reset();
        }
    }

    private sealed class ReferenceSyncVersion : TestSyncVersion
    {
        public ReferenceSyncVersion(int id) : base(id) { }

        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}
