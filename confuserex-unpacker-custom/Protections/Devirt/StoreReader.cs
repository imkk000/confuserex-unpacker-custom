using System;
using System.IO;
using System.Text;

namespace Protections.Devirt
{
    public sealed class PositionalXorStream : Stream
    {
        private readonly Stream inner;
        private readonly uint mask;

        public PositionalXorStream(Stream inner, int key)
        {
            this.inner = inner;
            this.mask = (uint)(key ^ -559030707);
        }

        private byte Transform(byte value, uint counter)
        {
            byte b = (byte)(this.mask ^ counter);
            return (byte)(value ^ b);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            uint counter = (uint)this.inner.Position;
            int read = this.inner.Read(buffer, offset, count);
            int end = offset + read;
            for (int i = offset; i < end; i++)
            {
                buffer[i] = Transform(buffer[i], counter++);
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => this.inner.Seek(offset, origin);
        public override bool CanRead => this.inner.CanRead;
        public override bool CanSeek => this.inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => this.inner.Length;
        public override long Position { get => this.inner.Position; set => this.inner.Position = value; }
        public override void Flush() => this.inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public sealed class StoreReader
    {
        private readonly Stream stream;
        private readonly byte[] scratch = new byte[16];

        public StoreReader(Stream stream)
        {
            this.stream = stream;
        }

        public Stream BaseStream => this.stream;

        public long Position { get => this.stream.Position; set => this.stream.Position = value; }

        private void Fill(int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = this.stream.Read(this.scratch, read, count - read);
                if (n == 0)
                {
                    throw new EndOfStreamException();
                }
                read += n;
            }
        }

        public byte ReadByte8()
        {
            int b = this.stream.ReadByte();
            if (b < 0)
            {
                throw new EndOfStreamException();
            }
            return (byte)b;
        }

        public int Read7BitEncodedInt()
        {
            int value = 0;
            int shift = 0;
            byte b;
            do
            {
                if (shift == 35)
                {
                    throw new FormatException();
                }
                b = ReadByte8();
                value |= (b & 0x7F) << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);
            return value;
        }

        public ushort ReadUInt16()
        {
            Fill(2);
            byte[] b = this.scratch;
            return (ushort)(b[1] | (b[0] << 8));
        }

        public int ReadInt32()
        {
            Fill(4);
            byte[] b = this.scratch;
            return b[0] | (b[3] << 24) | (b[1] << 16) | (b[2] << 8);
        }

        public uint ReadUInt32()
        {
            Fill(4);
            byte[] b = this.scratch;
            return (uint)((b[3] << 16) | b[1] | (b[0] << 8) | (b[2] << 24));
        }

        public long ReadInt64()
        {
            Fill(8);
            byte[] b = this.scratch;
            uint low = (uint)((b[7] << 8) | (b[2] << 24) | b[0] | (b[1] << 16));
            int high = (b[5] << 24) | (b[6] << 16) | b[4] | (b[3] << 8);
            return (long)low | ((long)high << 32);
        }

        public double ReadDouble()
        {
            Fill(8);
            byte[] a = this.scratch;
            byte[] o = new byte[8];
            o[0] = a[2];
            o[1] = a[5];
            o[2] = a[4];
            o[3] = a[3];
            o[4] = a[6];
            o[5] = a[1];
            o[6] = a[7];
            o[7] = a[0];
            return BitConverter.ToDouble(o, 0);
        }

        public string ReadString()
        {
            int length = Read7BitEncodedInt();
            byte[] data = ReadBytes(length);
            return Encoding.UTF8.GetString(data);
        }

        public byte[] ReadBytes(int count)
        {
            byte[] data = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = this.stream.Read(data, read, count - read);
                if (n == 0)
                {
                    throw new EndOfStreamException();
                }
                read += n;
            }
            return data;
        }
    }

    public static class Ascii85
    {
        private static readonly uint[] Pow = { 52200625u, 614125u, 7225u, 85u, 1u };

        public static byte[] Decode(string text)
        {
            using MemoryStream output = new MemoryStream(text.Length * 4 / 5);
            int count = 0;
            uint accum = 0u;
            foreach (char c in text)
            {
                if (c == 'z' && count == 0)
                {
                    WriteGroup(output, accum, 0);
                    continue;
                }
                if (c < '!' || c > 'u')
                {
                    throw new FormatException();
                }
                accum = checked(accum + (uint)(Pow[count] * (c - 33)));
                count++;
                if (count == 5)
                {
                    WriteGroup(output, accum, 0);
                    count = 0;
                    accum = 0u;
                }
            }
            if (count == 1)
            {
                throw new FormatException();
            }
            if (count > 1)
            {
                for (int j = count; j < 5; j++)
                {
                    accum = checked(accum + 84u * Pow[j]);
                }
                WriteGroup(output, accum, 5 - count);
            }
            return output.ToArray();
        }

        private static void WriteGroup(Stream output, uint value, int omit)
        {
            output.WriteByte((byte)(value >> 24));
            if (omit == 3)
            {
                return;
            }
            output.WriteByte((byte)(value >> 16));
            if (omit != 2)
            {
                output.WriteByte((byte)(value >> 8));
                if (omit != 1)
                {
                    output.WriteByte((byte)value);
                }
            }
        }
    }

    public static class MethodStore
    {
        public const int StoreXorKey = 1090939556;
        public const int KeyXorKey = 1492974841;

        public static long KeyToOffset(string proxyKey)
        {
            byte[] decoded = Ascii85.Decode(proxyKey);
            StoreReader reader = new StoreReader(new PositionalXorStream(new MemoryStream(decoded), KeyXorKey));
            return reader.ReadInt64();
        }

        public static StoreReader OpenStore(byte[] decryptedStore)
        {
            return new StoreReader(new PositionalXorStream(new MemoryStream(decryptedStore), StoreXorKey));
        }
    }
}
