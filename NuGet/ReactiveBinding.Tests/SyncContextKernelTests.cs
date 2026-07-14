#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ReactiveBinding;

namespace ReactiveBinding.Tests;

[TestFixture]
public class SyncContextKernelTests
{
    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(127, 1)]
    [TestCase(128, 2)]
    [TestCase(16_383, 2)]
    [TestCase(16_384, 3)]
    [TestCase(2_097_151, 3)]
    [TestCase(2_097_152, 4)]
    [TestCase(int.MaxValue, 5)]
    [TestCase(-1, 5)]
    [TestCase(int.MinValue, 5)]
    public void SyncWire_VarInt32RoundTrips(int value, int expectedBytes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        SyncWire.WriteVarInt32(writer, value);

        Assert.That(stream.Length, Is.EqualTo(expectedBytes));
        Assert.That(SyncWire.GetVarInt32Size(value), Is.EqualTo(expectedBytes));
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.That(SyncWire.ReadVarInt32(reader), Is.EqualTo(value));
    }

    [TestCase(0U, 1)]
    [TestCase(127U, 1)]
    [TestCase(128U, 2)]
    [TestCase(16_383U, 2)]
    [TestCase(16_384U, 3)]
    [TestCase(uint.MaxValue, 5)]
    public void SyncWire_VarUInt32RoundTrips(uint value, int expectedBytes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        SyncWire.WriteVarUInt32(writer, value);

        Assert.That(stream.Length, Is.EqualTo(expectedBytes));
        Assert.That(SyncWire.GetVarUInt32Size(value), Is.EqualTo(expectedBytes));
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.That(SyncWire.ReadVarUInt32(reader), Is.EqualTo(value));
    }

    [TestCase(0L, 1)]
    [TestCase(127L, 1)]
    [TestCase(128L, 2)]
    [TestCase(16_383L, 2)]
    [TestCase(16_384L, 3)]
    [TestCase(long.MaxValue, 9)]
    [TestCase(-1L, 10)]
    [TestCase(long.MinValue, 10)]
    public void SyncWire_VarInt64RoundTripsWithTwosComplement(long value, int expectedBytes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // net8's BinaryWriter instance method has the same name; static syntax verifies this implementation.
        SyncWire.WriteVarInt64(writer, value);

        Assert.That(stream.Length, Is.EqualTo(expectedBytes));
        Assert.That(SyncWire.GetVarInt64Size(value), Is.EqualTo(expectedBytes));
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.That(SyncWire.ReadVarInt64(reader), Is.EqualTo(value));
    }

    [TestCase(0UL, 1)]
    [TestCase(127UL, 1)]
    [TestCase(128UL, 2)]
    [TestCase(16_383UL, 2)]
    [TestCase(16_384UL, 3)]
    [TestCase(9_223_372_036_854_775_807UL, 9)]
    [TestCase(9_223_372_036_854_775_808UL, 10)]
    [TestCase(ulong.MaxValue, 10)]
    public void SyncWire_VarUInt64RoundTrips(ulong value, int expectedBytes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        SyncWire.WriteVarUInt64(writer, value);

        Assert.That(stream.Length, Is.EqualTo(expectedBytes));
        Assert.That(SyncWire.GetVarUInt64Size(value), Is.EqualTo(expectedBytes));
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.That(SyncWire.ReadVarUInt64(reader), Is.EqualTo(value));
    }

    [Test]
    public void SyncWire_VarCodecsAreOrdinaryStaticMethods()
    {
        string[] methodNames =
        {
            nameof(SyncWire.ReadVarInt32),
            nameof(SyncWire.WriteVarInt32),
            nameof(SyncWire.ReadVarUInt32),
            nameof(SyncWire.WriteVarUInt32),
            nameof(SyncWire.ReadVarInt64),
            nameof(SyncWire.WriteVarInt64),
            nameof(SyncWire.ReadVarUInt64),
            nameof(SyncWire.WriteVarUInt64)
        };

        Assert.Multiple(() =>
        {
            foreach (string methodName in methodNames)
            {
                var method = typeof(SyncWire).GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                Assert.That(method, Is.Not.Null, $"Missing SyncWire.{methodName}.");
                if (method == null)
                {
                    continue;
                }

                Assert.That(
                    method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), inherit: false),
                    Is.False,
                    $"SyncWire.{methodName} must remain an ordinary static method.");
            }
        });
    }

    [Test]
    public void SyncWire_SignedEncodingsUseTwosComplementBytes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EncodeVarInt32(-1),
                Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }));
            Assert.That(EncodeVarInt32(int.MinValue),
                Is.EqualTo(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 }));
            Assert.That(EncodeVarInt64(-1),
                Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 }));
            Assert.That(EncodeVarInt64(long.MinValue),
                Is.EqualTo(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 }));
        });
    }

    [Test]
    public void SyncWire_VarInt32AndVarUInt32RejectTruncatedAndOverflowPayloads()
    {
        Assert.Throws<EndOfStreamException>(() => DecodeVarInt32(0x80));
        Assert.Throws<EndOfStreamException>(() => DecodeVarUInt32(0x80));
        Assert.Throws<InvalidDataException>(() => DecodeVarInt32(0x80, 0x80, 0x80, 0x80, 0x10));
        Assert.Throws<InvalidDataException>(() => DecodeVarUInt32(0x80, 0x80, 0x80, 0x80, 0x10));
    }

    [Test]
    public void SyncWire_VarInt64AndVarUInt64RejectTruncatedAndOverflowPayloads()
    {
        Assert.Throws<EndOfStreamException>(() => DecodeVarInt64(0x80));
        Assert.Throws<EndOfStreamException>(() => DecodeVarUInt64(0x80));
        byte[] overflow = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 };
        Assert.Throws<InvalidDataException>(() => DecodeVarInt64(overflow));
        Assert.Throws<InvalidDataException>(() => DecodeVarUInt64(overflow));
    }

    [Test]
    public void AllocateId_ReturnsPositiveIdsAndDoesNotOverflow()
    {
        var context = new SyncContext();

        Assert.That(context.__AllocateId(), Is.EqualTo(1));
        Assert.That(context.__AllocateId(), Is.EqualTo(2));

        context.__NextId = int.MaxValue;
        Assert.Throws<InvalidOperationException>(() => context.__AllocateId());
        Assert.That(context.__NextId, Is.EqualTo(int.MaxValue));
        Assert.That(context.__NextId, Is.GreaterThan(0));
    }

    [Test]
    public void CaptureDelta_UsesUniqueDirtyIdsInParentBeforeChildOrder()
    {
        var context = new SyncContext();
        var child = Register(context, 2, 22);
        var parent = Register(context, 1, 11);
        child.SetDirty(222);
        parent.SetDirty(111);

        context.__EnlistDirty(0);
        context.__EnlistDirty(999);
        context.__EnlistDirty(parent.__SyncId);

        var frame = CaptureDelta(context);
        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        Assert.That(reader.ReadByte(), Is.Zero);
        Assert.That(SyncWire.ReadVarInt32(reader), Is.EqualTo(1));
        Assert.That(reader.ReadInt32(), Is.EqualTo(111));
        Assert.That(SyncWire.ReadVarInt32(reader), Is.EqualTo(2));
        Assert.That(reader.ReadInt32(), Is.EqualTo(222));
        Assert.That(SyncWire.ReadVarInt32(reader), Is.Zero, "normal-record terminator");
        Assert.That(SyncWire.ReadVarInt32(reader), Is.Zero, "tombstone count");
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
        Assert.That(parent.CaptureDeltaCalls, Is.EqualTo(1));
        Assert.That(child.CaptureDeltaCalls, Is.EqualTo(1));
        Assert.That(parent.__IsDirty, Is.False);
        Assert.That(child.__IsDirty, Is.False);

        Assert.That(CaptureDelta(context), Is.EqualTo(new byte[] { 0, 0, 0 }));
    }

    [Test]
    public void CaptureDelta_DoesNotProbeCleanRegistryNodes()
    {
        var context = new SyncContext();
        var nodes = new List<KernelNode>();
        for (int id = 1; id <= 500; id++) nodes.Add(Register(context, id, id));

        nodes[349].SetDirty(999);
        foreach (var node in nodes) node.ResetDirtyChecks();

        CaptureDelta(context);

        Assert.That(nodes[349].DirtyChecks, Is.EqualTo(1));
        for (int i = 0; i < nodes.Count; i++)
            if (i != 349) Assert.That(nodes[i].DirtyChecks, Is.Zero, $"clean node {i + 1} was probed");
    }

    [Test]
    public void Apply_AppliesNormalRecordsBeforeTombstones()
    {
        var producer = new SyncContext();
        var producerParent = Register(producer, 1, 10);
        var removed = Register(producer, 2, 20);
        producerParent.SetDirty(99);
        int removedId = removed.__SyncId;
        producer.__RecordTombstone(removedId);
        producer.__RecordTombstone(removedId);
        removed.__Reset();

        var trace = new List<string>();
        var consumer = new SyncContext();
        var consumerParent = Register(consumer, 1, 0, trace);
        Register(consumer, 2, 0, trace);

        Apply(consumer, CaptureDelta(producer));

        Assert.That(consumerParent.Value, Is.EqualTo(99));
        Assert.That(consumer.__Objects.ContainsKey(2), Is.False);
        Assert.That(trace, Is.EqualTo(new[] { "apply:1", "reset:2" }));
    }

    [Test]
    public void CaptureFull_SupersedesPendingDirtyIdsAndTombstones()
    {
        var context = new SyncContext();
        var node = Register(context, 1, 7);
        node.SetDirty(8);
        context.__RecordTombstone(42);

        context.TrimScratch();
        context.Compact();
        var frame = CaptureFull(context);

        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(SyncWire.ReadVarInt32(reader), Is.EqualTo(1));
        Assert.That(reader.ReadInt32(), Is.EqualTo(8));
        Assert.That(SyncWire.ReadVarInt32(reader), Is.Zero);
        Assert.That(SyncWire.ReadVarInt32(reader), Is.Zero);
        Assert.That(node.__IsDirty, Is.False);
        Assert.That(context.__Objects[1], Is.SameAs(node));
        Assert.That(CaptureDelta(context), Is.EqualTo(new byte[] { 0, 0, 0 }));
    }

    [Test]
    public void CompactAndTrimScratchPreservePendingDeltaAndTombstone()
    {
        var context = new SyncContext();
        var node = Register(context, 1, 7);
        node.SetDirty(8);
        context.__RecordTombstone(200);

        context.TrimScratch();
        context.Compact();
        var frame = CaptureDelta(context);

        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        Assert.That(reader.ReadByte(), Is.Zero);
        Assert.That(SyncWire.ReadVarInt32(reader), Is.EqualTo(1));
        Assert.That(reader.ReadInt32(), Is.EqualTo(8));
        Assert.That(SyncWire.ReadVarInt32(reader), Is.Zero);
        Assert.That(SyncWire.ReadVarInt32(reader), Is.EqualTo(1));
        Assert.That(SyncWire.ReadVarInt32(reader), Is.EqualTo(200));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [Test]
    public void Apply_ConsumesExactlyOneFrameFromNonSeekableStream()
    {
        var producer = new SyncContext();
        Register(producer, 1, 123);
        var consumer = new SyncContext();
        var target = Register(consumer, 1, 0);
        var frame = CaptureFull(producer);

        using var backing = new MemoryStream(frame);
        using var nonSeekable = new NonSeekableReadStream(backing);
        using var reader = new BinaryReader(nonSeekable);
        consumer.Apply(reader);

        Assert.That(target.Value, Is.EqualTo(123));
    }

    [Test]
    public void Apply_CoalescesTouchedNodeAndAncestorVersionsPerFrame()
    {
        var producer = new SyncContext();
        var producerParent = Register(producer, 1, 1);
        var producerChild = Register(producer, 2, 2);
        producerChild.__Parent = producerParent;
        producerParent.SetDirty(10);
        producerChild.SetDirty(20);

        var consumer = new SyncContext();
        var consumerParent = Register(consumer, 1, 0);
        var consumerChild = Register(consumer, 2, 0);
        consumerChild.__Parent = consumerParent;

        Apply(consumer, CaptureDelta(producer));

        Assert.That(consumerParent.__Version, Is.GreaterThan(0));
        Assert.That(consumerChild.__Version, Is.EqualTo(consumerParent.__Version));
        Assert.That(consumerParent.SetVersionCalls, Is.EqualTo(1));
        Assert.That(consumerChild.SetVersionCalls, Is.EqualTo(1));
    }

    [Test]
    public void Apply_ConsumesOneOfMultipleConcatenatedFramesPerCall()
    {
        var producer = new SyncContext();
        var source = Register(producer, 1, 0);
        source.SetDirty(10);
        var first = CaptureDelta(producer);
        source.SetDirty(20);
        var second = CaptureDelta(producer);

        var consumer = new SyncContext();
        var target = Register(consumer, 1, 0);
        using var stream = new MemoryStream();
        stream.Write(first, 0, first.Length);
        stream.Write(second, 0, second.Length);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);

        consumer.Apply(reader);
        Assert.That(target.Value, Is.EqualTo(10));
        Assert.That(stream.Position, Is.EqualTo(first.Length));
        consumer.Apply(reader);
        Assert.That(target.Value, Is.EqualTo(20));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    private static KernelNode Register(SyncContext context, int id, int value, List<string> trace = null)
    {
        var node = new KernelNode(value, trace)
        {
            __SyncId = id,
            __SyncContext = context,
        };
        context.__Objects[id] = node;
        if (id >= context.__NextId) context.__NextId = id + 1;
        return node;
    }

    private static byte[] CaptureFull(SyncContext context)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        context.CaptureFull(writer);
        return stream.ToArray();
    }

    private static byte[] CaptureDelta(SyncContext context)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        context.CaptureDelta(writer);
        return stream.ToArray();
    }

    private static void Apply(SyncContext context, byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        context.Apply(reader);
    }

    private static byte[] EncodeVarInt32(int value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        SyncWire.WriteVarInt32(writer, value);
        return stream.ToArray();
    }

    private static byte[] EncodeVarInt64(long value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        SyncWire.WriteVarInt64(writer, value);
        return stream.ToArray();
    }

    private static int DecodeVarInt32(params byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        return SyncWire.ReadVarInt32(reader);
    }

    private static uint DecodeVarUInt32(params byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        return SyncWire.ReadVarUInt32(reader);
    }

    private static long DecodeVarInt64(params byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        return SyncWire.ReadVarInt64(reader);
    }

    private static ulong DecodeVarUInt64(params byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        return SyncWire.ReadVarUInt64(reader);
    }

    private sealed class KernelNode : IVersionSync
    {
        private readonly List<string> _trace;
        private bool _isDirty;
        private int _version;

        public KernelNode(int value, List<string> trace = null)
        {
            Value = value;
            _trace = trace;
        }

        public int Value { get; private set; }
        public int CaptureDeltaCalls { get; private set; }
        public int SetVersionCalls { get; private set; }
        public int DirtyChecks { get; private set; }
        public int Version => __Version;
        public int __Version
        {
            get => _version;
            set
            {
                _version = value;
                SetVersionCalls++;
            }
        }
        public IVersion __Parent { get; set; }
        public int __SyncId { get; set; }
        public SyncContext __SyncContext { get; set; }
        public bool __IsDirty
        {
            get { DirtyChecks++; return _isDirty; }
            private set => _isDirty = value;
        }
        public void ResetDirtyChecks() => DirtyChecks = 0;

        public void SetDirty(int value)
        {
            Value = value;
            if (__IsDirty) return;
            __IsDirty = true;
            __SyncContext?.__EnlistDirty(__SyncId);
        }

        public void AttachTo(SyncContext context)
        {
            __SyncContext = context;
            if (__SyncId == 0) __SyncId = context.__AllocateId();
            context.__Objects[__SyncId] = this;
            __MarkAllDirty();
        }

        public void __CaptureFull(BinaryWriter writer)
        {
            SyncWire.WriteVarInt32(writer, __SyncId);
            writer.Write(Value);
        }

        public void __CaptureDelta(BinaryWriter writer)
        {
            CaptureDeltaCalls++;
            __CaptureFull(writer);
        }

        public void __ClearDirty() => __IsDirty = false;

        public void __MarkAllDirty()
        {
            if (__IsDirty) return;
            __IsDirty = true;
            __SyncContext?.__EnlistDirty(__SyncId);
        }

        public void __Apply(BinaryReader reader)
        {
            Value = reader.ReadInt32();
            __SyncContext!.__TouchVersion(this);
            _trace?.Add($"apply:{__SyncId}");
        }

        public void __SyncChildren(SyncOp op) { }

        public void __IncrementVersion()
        {
            _version++;
            __Parent?.__IncrementVersion();
        }

        public void __Reset()
        {
            int oldId = __SyncId;
            _trace?.Add($"reset:{oldId}");
            if (__SyncContext != null) __SyncContext.__Objects.Remove(oldId);
            __SyncId = 0;
            __SyncContext = null;
            __IsDirty = false;
            _version = 0;
            __Parent = null;
        }

        public void Reset() => __Reset();
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableReadStream(Stream inner) => _inner = inner;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int ReadByte() => _inner.ReadByte();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
