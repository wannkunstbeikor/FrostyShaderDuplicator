using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FrostyShaderDuplicator
{
    internal class Program
    {
        public class Shader
        {
            public string shader { get; set; }
            public byte[] dxbc { get; set; }
        }
        static void Main(string[] args)
        {
            Shader[] vShaders;
            Shader[] pShaders;

            if (args[0].Equals("-help"))
            {
                Help();
                return;
            }

            using (NativeReader reader = new NativeReader(File.OpenRead(args[1])))
            {
                Console.WriteLine("reading shader.bin");
                int numVEntries = reader.ReadInt();
                vShaders = new Shader[numVEntries + 1];
                long startVOffset = reader.ReadLong();
                int numPEntries = reader.ReadInt();
                pShaders = new Shader[numPEntries + 1];
                long startPOffset = reader.ReadLong();

                reader.Position = startVOffset;

                for (int i = 0; i < numVEntries; i++)
                {
                    Shader s = new Shader();
                    s.shader = reader.ReadNullTerminatedString();
                    int size = reader.ReadInt();
                    long offset = reader.ReadLong();

                    long curPos = reader.Position;

                    reader.Position = offset;
                    s.dxbc = reader.ReadBytes(size);

                    vShaders[i] = s;

                    if (args[0].Equals("-dupe"))
                    {
                        if (s.shader == args[3])
                        {
                            Shader sq = new Shader();
                            sq.shader = args[4];
                            sq.dxbc = s.dxbc;
                            vShaders[numVEntries] = sq;
                        }
                    }

                    reader.Position = curPos;
                }
                for (int i = 0; i < numPEntries; i++)
                {
                    Shader s = new Shader();
                    s.shader = reader.ReadNullTerminatedString();
                    int size = reader.ReadInt();
                    long offset = reader.ReadLong();

                    long curPos = reader.Position;

                    reader.Position = offset;
                    s.dxbc = reader.ReadBytes(size);

                    pShaders[i] = s;

                    if (args[0].Equals("-dupe"))
                    {
                        if (s.shader == args[3])
                        {
                            Shader sq = new Shader();
                            sq.shader = args[4];
                            sq.dxbc = s.dxbc;
                            pShaders[numPEntries] = sq;
                        }
                    }

                    reader.Position = curPos;
                }
            }

            if (args[0].Equals("-add"))
            {
                Shader ps = new Shader();
                ps.shader = args[5];
                using (NativeReader reader = new NativeReader(File.OpenRead(args[3])))
                    ps.dxbc = reader.ReadToEnd();

                pShaders[pShaders.Length - 1] = ps;

                Shader vs = new Shader();
                vs.shader = args[5];
                using (NativeReader reader = new NativeReader(File.OpenRead(args[4])))
                    vs.dxbc = reader.ReadToEnd();

                vShaders[vShaders.Length - 1] = vs;
            }

            using (NativeWriter writer = new NativeWriter(File.OpenWrite(args[2])))
            {
                List<long> offsets = new List<long>();
                writer.Write(vShaders.Length);
                writer.Write((long)0x18);
                writer.Write(pShaders.Length);
                writer.Write(0xdeadbeefdeadbeef);

                foreach (Shader s in vShaders)
                {
                    writer.WriteNullTerminatedString(s.shader);
                    writer.Write(s.dxbc.Length);
                    offsets.Add(writer.Position);
                    writer.Write(0xdeadbeefdeadbeef);
                }
                long curPos = writer.Position;
                writer.Position = 0x10;
                writer.Write(curPos);
                writer.Position = curPos;
                foreach (Shader s in pShaders)
                {
                    writer.WriteNullTerminatedString(s.shader);
                    writer.Write(s.dxbc.Length);
                    offsets.Add(writer.Position);
                    writer.Write(0xdeadbeefdeadbeef);
                }
                foreach (Shader s in vShaders)
                {
                    curPos = writer.Position;
                    writer.Position = offsets[0];
                    writer.Write(curPos);
                    writer.Position = curPos;
                    offsets.RemoveAt(0);
                    writer.Write(s.dxbc);
                }
                foreach (Shader s in pShaders)
                {
                    curPos = writer.Position;
                    writer.Position = offsets[0];
                    writer.Write(curPos);
                    writer.Position = curPos;
                    offsets.RemoveAt(0);
                    writer.Write(s.dxbc);
                }
            }
            Console.WriteLine($"wrote new shader.bin to {args[2]}");
        }
        static void Help()
        {
            Console.WriteLine("FrostyShaderDuplicator");
            Console.WriteLine();
            Console.WriteLine("Add shaders for game profiles to the Shader.bin Frosty uses");
            Console.WriteLine();
            Console.WriteLine("actions");
            Console.WriteLine("-dupe: dupes a profile shader for a new profile");
            Console.WriteLine(@"usage: .\FrostyShaderDuplicator -dupe <path to shader.bin> <new shader.bin path> <profile version to dupe> <new profile version>");
            Console.WriteLine();
            Console.WriteLine("-add: adds a profile shader");
            Console.WriteLine(@"usage: .\FrostyShaderDuplicator -add <path to shader.bin> <new shader.bin path> <path to pixel shader dxbc> <path to vertex shader dxbc> <new profile version>");
            Console.WriteLine();
        }
    }
    public enum Endian
    {
        Little,
        Big
    }

    public class NativeReader : IDisposable
    {
        public Stream BaseStream => stream;

        public virtual long Position
        {
            get => stream?.Position ?? 0;
            set
            {
                stream.Position = value;
            }
        }
        public virtual long Length => streamLength;

        protected Stream stream;
        protected byte[] buffer;
        protected char[] charBuffer;
        protected long streamLength;
        protected Encoding wideDecoder;

        public NativeReader(Stream inStream)
        {
            stream = inStream;
            if (stream != null)
                streamLength = stream.Length;

            wideDecoder = new UnicodeEncoding();
            buffer = new byte[20];
            charBuffer = new char[2];
        }

        public static byte[] ReadInStream(Stream inStream)
        {
            using (NativeReader reader = new NativeReader(inStream))
                return reader.ReadToEnd();
        }

        #region -- Basic Types --

        public char ReadWideChar()
        {
            FillBuffer(2);
            wideDecoder.GetChars(buffer, 0, 2, charBuffer, 0);
            return charBuffer[0];
        }

        public bool ReadBoolean() => ReadByte() == 1;

        public byte ReadByte()
        {
            FillBuffer(1);
            return buffer[0];
        }

        public sbyte ReadSByte()
        {
            FillBuffer(1);
            return (sbyte)buffer[0];
        }

        public short ReadShort(Endian inEndian = Endian.Little)
        {
            FillBuffer(2);
            if (inEndian == Endian.Little)
                return (short)(buffer[0] | buffer[1] << 8);
            return (short)(buffer[1] | buffer[0] << 8);
        }

        public ushort ReadUShort(Endian inEndian = Endian.Little)
        {
            FillBuffer(2);
            if (inEndian == Endian.Little)
                return (ushort)(buffer[0] | buffer[1] << 8);
            return (ushort)(buffer[1] | buffer[0] << 8);
        }

        public int ReadInt(Endian inEndian = Endian.Little)
        {
            FillBuffer(4);
            if (inEndian == Endian.Little)
                return (int)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24);
            return (int)(buffer[3] | buffer[2] << 8 | buffer[1] << 16 | buffer[0] << 24);
        }

        public uint ReadUInt(Endian inEndian = Endian.Little)
        {
            FillBuffer(4);
            if (inEndian == Endian.Little)
                return (uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24);
            return (uint)(buffer[3] | buffer[2] << 8 | buffer[1] << 16 | buffer[0] << 24);
        }

        public long ReadLong(Endian inEndian = Endian.Little)
        {
            FillBuffer(8);
            if (inEndian == Endian.Little)
                return (long)(uint)(buffer[4] | buffer[5] << 8 | buffer[6] << 16 | buffer[7] << 24) << 32 |
                       (long)(uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24);
            return (long)(uint)(buffer[3] | buffer[2] << 8 | buffer[1] << 16 | buffer[0] << 24) << 32 |
                   (long)(uint)(buffer[7] | buffer[6] << 8 | buffer[5] << 16 | buffer[4] << 24);
        }

        public ulong ReadULong(Endian inEndian = Endian.Little)
        {
            FillBuffer(8);
            if (inEndian == Endian.Little)
                return (ulong)(uint)(buffer[4] | buffer[5] << 8 | buffer[6] << 16 | buffer[7] << 24) << 32 |
                       (ulong)(uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24);
            return (ulong)(uint)(buffer[3] | buffer[2] << 8 | buffer[1] << 16 | buffer[0] << 24) << 32 |
                   (ulong)(uint)(buffer[7] | buffer[6] << 8 | buffer[5] << 16 | buffer[4] << 24);
        }

        public unsafe float ReadFloat(Endian inEndian = Endian.Little)
        {
            FillBuffer(4);

            uint tmpBuffer = 0;
            if (inEndian == Endian.Little)
                tmpBuffer = (uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24);
            else
                tmpBuffer = (uint)(buffer[3] | buffer[2] << 8 | buffer[1] << 16 | buffer[0] << 24);

            return *((float*)&tmpBuffer);
        }

        public unsafe double ReadDouble(Endian inEndian = Endian.Little)
        {
            FillBuffer(8);

            uint lo = 0;
            uint hi = 0;

            if (inEndian == Endian.Little)
            {
                lo = (uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24);
                hi = (uint)(buffer[4] | buffer[5] << 8 | buffer[6] << 16 | buffer[7] << 24);
            }
            else
            {
                lo = (uint)(buffer[3] | buffer[2] << 8 | buffer[1] << 16 | buffer[0] << 24);
                hi = (uint)(buffer[7] | buffer[6] << 8 | buffer[5] << 16 | buffer[4] << 24);
            }

            ulong tmpBuffer = ((ulong)hi) << 32 | lo;
            return *((double*)&tmpBuffer);
        }

        #endregion

        #region -- Special Types --

        public Guid ReadGuid(Endian endian = Endian.Little)
        {
            FillBuffer(16);
            if (endian == Endian.Little)
                return new Guid(new byte[] {
                        buffer[0], buffer[1], buffer[2], buffer[3], buffer[4], buffer[5], buffer[6], buffer[7],
                        buffer[8], buffer[9], buffer[10], buffer[11], buffer[12], buffer[13], buffer[14], buffer[15]
                    });

            return new Guid(new byte[] {
                    buffer[3], buffer[2], buffer[1], buffer[0], buffer[5], buffer[4], buffer[7], buffer[6],
                    buffer[8], buffer[9], buffer[10], buffer[11], buffer[12], buffer[13], buffer[14], buffer[15]
                });
        }

        public int Read7BitEncodedInt()
        {
            int result = 0;
            int i = 0;

            while (true)
            {
                int b = ReadByte();
                result |= (b & 127) << i;

                if (b >> 7 == 0)
                    return result;

                i += 7;
            }
        }

        public long Read7BitEncodedLong()
        {
            long result = 0;
            int i = 0;

            while (true)
            {
                int b = ReadByte();
                result |= (long)((b & 127) << i);

                if (b >> 7 == 0)
                    return result;

                i += 7;
            }
        }

        #endregion

        #region -- String Types --

        public string ReadNullTerminatedString()
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                char c = (char)ReadByte();
                if (c == 0x00)
                    return sb.ToString();

                sb.Append(c);
            }
        }

        public string ReadNullTerminatedWideString()
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                char c = ReadWideChar();
                if (c == 0x0000)
                    return sb.ToString();

                sb.Append(c);
            }
        }

        public string ReadSizedString(int strLen)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < strLen; i++)
            {
                char c = (char)ReadByte();
                if (c != 0x00)
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public string ReadLine()
        {
            StringBuilder sb = new StringBuilder();
            byte c = 0x00;

            while (c != 0x0d && c != 0x0a)
            {
                c = ReadByte();
                sb.Append((char)c);
                if (c == 0x0a || c == 0x0d || Position >= Length)
                    break;
            }

            if (c == 0x0d)
                ReadByte();

            return sb.ToString().Trim('\r', '\n');
        }

        public string ReadWideLine()
        {
            StringBuilder sb = new StringBuilder();
            char c = (char)0x00;

            while (c != 0x0d && c != 0x0a)
            {
                c = ReadWideChar();
                sb.Append(c);
                if (c == 0x0a || c == 0x0d || Position >= Length)
                    break;
            }

            if (c == 0x0d)
                ReadWideChar();

            return sb.ToString().Trim('\r', '\n');
        }

        public void Pad(int alignment)
        {
            while (Position % alignment != 0)
                Position++;
        }

        #endregion

        public byte[] ReadToEnd()
        {
            long totalSize = Length - Position;
            if (totalSize < int.MaxValue)
                return ReadBytes((int)totalSize);

            byte[] outBuffer = new byte[totalSize];
            while (totalSize > 0)
            {
                int bufferSize = (totalSize > int.MaxValue) ? int.MaxValue : (int)totalSize;
                byte[] tmpBuffer = new byte[bufferSize];

                int count = Read(tmpBuffer, 0, bufferSize);
                totalSize -= bufferSize;

                Buffer.BlockCopy(tmpBuffer, 0, outBuffer, count, bufferSize);
            }

            return outBuffer;
        }

        public byte[] ReadBytes(int count)
        {
            byte[] outBuffer = new byte[count];
            int totalNumBytesRead = 0;

            do
            {
                int numBytesRead = Read(outBuffer, totalNumBytesRead, count);
                if (numBytesRead == 0)
                    break;

                totalNumBytesRead += numBytesRead;
                count -= numBytesRead;

            } while (count > 0);

            return outBuffer;
        }

        public virtual int Read(byte[] inBuffer, int offset, int numBytes)
        {
            int count = stream.Read(inBuffer, offset, numBytes);
            return count;
        }

        public Stream CreateViewStream(long offset, long size)
        {
            Position = offset;
            return new MemoryStream(ReadBytes((int)size));
        }

        public void Dispose() => Dispose(true);

        protected virtual void FillBuffer(int numBytes)
        {
            stream.Read(buffer, 0, numBytes);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stream copyOfStream = stream;
                stream = null;

                copyOfStream?.Close();
            }

            stream = null;
            buffer = null;
        }
    }
    public class NativeWriter : BinaryWriter
    {
        public long Position { get => BaseStream.Position; set => BaseStream.Position = value; }
        public long Length => BaseStream.Length;

        public NativeWriter(Stream inStream, bool leaveOpen = false, bool wide = false)
            : base(inStream, (wide) ? Encoding.Unicode : Encoding.Default, leaveOpen)
        {
        }

        public void Write(Guid value, Endian endian)
        {
            if (endian == Endian.Big)
            {
                byte[] b = value.ToByteArray();
                Write(b[3]); Write(b[2]); Write(b[1]); Write(b[0]);
                Write(b[5]); Write(b[4]);
                Write(b[7]); Write(b[6]);
                for (int i = 0; i < 8; i++)
                    Write(b[8 + i]);
            }
            else
                Write(value);
        }

        public void Write(short value, Endian endian)
        {
            if (endian == Endian.Big)
                Write((short)((ushort)(((value & 0xFF) << 8) | ((value & 0xFF00) >> 8))));
            else
                Write(value);
        }

        public void Write(ushort value, Endian endian)
        {
            if (endian == Endian.Big)
                Write(((ushort)(((value & 0xFF) << 8) | ((value & 0xFF00) >> 8))));
            else
                Write(value);
        }

        public void Write(int value, Endian endian)
        {
            if (endian == Endian.Big)
                Write(((value & 0xFF) << 24) | ((value & 0xFF00) << 8) | ((value >> 8) & 0xFF00) | ((value >> 24) & 0xFF));
            else
                Write(value);
        }

        public void Write(uint value, Endian endian)
        {
            if (endian == Endian.Big)
                Write(((value & 0xFF) << 24) | ((value & 0xFF00) << 8) | ((value >> 8) & 0xFF00) | ((value >> 24) & 0xFF));
            else
                Write(value);
        }

        public void Write(long value, Endian endian)
        {
            if (endian == Endian.Big)
            {
                Write(((long)((value & 0xFF) << 56) | ((value & 0xFF00) << 40) | ((value & 0xFF0000) << 24) | ((value & 0xFF000000) << 8))
                    | ((long)((value >> 8) & 0xFF000000) | ((value >> 24) & 0xFF0000) | ((value >> 40) & 0xFF00) | ((value >> 56) & 0xFF)));
            }
            else
                Write(value);
        }

        public void Write(ulong value, Endian endian)
        {
            if (endian == Endian.Big)
            {
                Write(((ulong)((value & 0xFF) << 56) | ((value & 0xFF00) << 40) | ((value & 0xFF0000) << 24) | ((value & 0xFF000000) << 8))
                    | ((ulong)((value >> 8) & 0xFF000000) | ((value >> 24) & 0xFF0000) | ((value >> 40) & 0xFF00) | ((value >> 56) & 0xFF)));
            }
            else
                Write(value);
        }

        private void WriteString(string str)
        {
            for (int i = 0; i < str.Length; i++)
                Write(str[i]);
        }

        public void WriteNullTerminatedString(string str)
        {
            WriteString(str);
            Write((char)0x00);
        }

        public void WriteSizedString(string str)
        {
            Write7BitEncodedInt(str.Length);
            WriteString(str);
        }

        public void WriteFixedSizedString(string str, int size)
        {
            WriteString(str);
            for (int i = 0; i < (size - str.Length); i++)
                Write((char)0x00);
        }

        public new void Write7BitEncodedInt(int value)
        {
            uint v = (uint)value;
            while (v >= 0x80)
            {
                Write((byte)(v | 0x80));
                v >>= 7;
            }
            Write((byte)v);
        }

        public void Write7BitEncodedLong(long value)
        {
            ulong v = (ulong)value;
            while (v >= 0x80)
            {
                Write((byte)(v | 0x80));
                v >>= 7;
            }
            Write((byte)v);
        }

        public void Write(Guid value) => Write(value.ToByteArray(), 0, 16);

        public void WriteLine(string str)
        {
            WriteString(str);
            Write((char)0x0D);
            Write((char)0x0A);
        }

        public void WritePadding(byte alignment)
        {
            while (Position % alignment != 0)
                Write((byte)0x00);
        }

        public byte[] ToByteArray() => BaseStream is MemoryStream stream ? stream.ToArray() : null;
    }
}
