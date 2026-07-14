using System;
using System.IO;

namespace ReactiveBinding
{
    /// <summary>Compact binary encoding extensions shared by generated sync nodes, containers, and <see cref="SyncContext"/>.</summary>
    public static class SyncWire
    {
        /// <summary>Writes an <see cref="int"/> as its two's-complement bits using 7-bit continuation encoding.</summary>
        public static void WriteVarInt32(this BinaryWriter writer, int value)
            => WriteVarUInt32(writer, unchecked((uint)value));

        /// <summary>Reads an <see cref="int"/> written by <see cref="WriteVarInt32"/>.</summary>
        public static int ReadVarInt32(this BinaryReader reader)
            => unchecked((int)ReadVarUInt32(reader));

        /// <summary>Returns the encoded byte count for an <see cref="int"/>.</summary>
        public static int GetVarInt32Size(int value)
            => GetVarUInt32Size(unchecked((uint)value));

        /// <summary>Writes a <see cref="uint"/> using 7-bit continuation encoding.</summary>
        public static void WriteVarUInt32(this BinaryWriter writer, uint value)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            while (value >= 0x80)
            {
                writer.Write((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        /// <summary>Reads a <see cref="uint"/> written by <see cref="WriteVarUInt32"/>.</summary>
        public static uint ReadVarUInt32(this BinaryReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            uint value = 0;
            for (int shift = 0; shift <= 28; shift += 7)
            {
                byte current = reader.ReadByte();
                if (shift == 28 && (current & 0xF0) != 0)
                    throw new InvalidDataException("The 7-bit encoded integer exceeds UInt32.MaxValue.");

                value |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0) return value;
            }

            throw new InvalidDataException("The 7-bit encoded integer is too long for UInt32.");
        }

        /// <summary>Returns the encoded byte count for a <see cref="uint"/>.</summary>
        public static int GetVarUInt32Size(uint value)
        {
            int size = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                size++;
            }
            return size;
        }

        /// <summary>Writes a <see cref="long"/> as its two's-complement bits using 7-bit continuation encoding.</summary>
        public static void WriteVarInt64(this BinaryWriter writer, long value)
            => WriteVarUInt64(writer, unchecked((ulong)value));

        /// <summary>Reads a <see cref="long"/> written by <see cref="WriteVarInt64"/>.</summary>
        public static long ReadVarInt64(this BinaryReader reader)
            => unchecked((long)ReadVarUInt64(reader));

        /// <summary>Returns the encoded byte count for a <see cref="long"/>.</summary>
        public static int GetVarInt64Size(long value)
            => GetVarUInt64Size(unchecked((ulong)value));

        /// <summary>Writes a <see cref="ulong"/> using 7-bit continuation encoding.</summary>
        public static void WriteVarUInt64(this BinaryWriter writer, ulong value)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            while (value >= 0x80)
            {
                writer.Write((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        /// <summary>Reads a <see cref="ulong"/> written by <see cref="WriteVarUInt64"/>.</summary>
        public static ulong ReadVarUInt64(this BinaryReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            ulong value = 0;
            for (int shift = 0; shift <= 63; shift += 7)
            {
                byte current = reader.ReadByte();
                if (shift == 63 && (current & 0xFE) != 0)
                    throw new InvalidDataException("The 7-bit encoded integer exceeds UInt64.MaxValue.");

                value |= (ulong)(current & 0x7F) << shift;
                if ((current & 0x80) == 0) return value;
            }

            throw new InvalidDataException("The 7-bit encoded integer is too long for UInt64.");
        }

        /// <summary>Returns the encoded byte count for a <see cref="ulong"/>.</summary>
        public static int GetVarUInt64Size(ulong value)
        {
            int size = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                size++;
            }
            return size;
        }

    }
}
