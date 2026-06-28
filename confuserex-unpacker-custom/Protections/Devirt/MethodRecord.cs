using System;
using System.Collections.Generic;
using System.IO;

namespace Protections.Devirt
{
    public sealed class ParamEntry
    {
        public int TypeId;
        public bool ByRef;
    }

    public sealed class MethodSignature
    {
        public List<ParamEntry> Parameters = new List<ParamEntry>();
        public List<int> GenericParameters = new List<int>();
        public List<int> Locals = new List<int>();
        public int ReturnTypeId;
        public byte Flags;
        public int DeclaringTypeId;
        public string Name;
    }

    public sealed class EhClause
    {
        public int ClauseType;
        public int CatchTypeId;
        public uint HandlerStart;
        public uint TryStart;
        public uint TryLength;
        public uint Extra;
    }

    public sealed class MethodRecord
    {
        public MethodSignature Signature;
        public List<EhClause> Handlers = new List<EhClause>();
        public byte[] Bytecode;
    }

    public sealed class MethodRecordReader
    {
        private readonly StoreReader reader;
        private readonly bool trace;

        public MethodRecordReader(StoreReader reader, bool trace)
        {
            this.reader = reader;
            this.trace = trace;
        }

        public StoreReader Reader => this.reader;

        private void Log(string label, object value)
        {
            if (this.trace)
            {
                Console.WriteLine("  @" + this.reader.Position.ToString("D6") + " " + label + " = " + value);
            }
        }

        public MethodRecord Read(long offset)
        {
            this.reader.Position = offset;
            if (this.trace)
            {
                Console.WriteLine("[record] offset=" + offset);
            }

            byte leading = this.reader.ReadByte8();
            Log("leadingByte", leading);

            MethodSignature sig = ReadSignature();
            List<EhClause> handlers = ReadHandlers();
            int codeLen = this.reader.ReadInt32();
            Log("bytecodeLen", codeLen);
            byte[] code = this.reader.ReadBytes(codeLen);

            return new MethodRecord { Signature = sig, Handlers = handlers, Bytecode = code };
        }

        public MethodSignature ReadSignatureRecord(long offset)
        {
            this.reader.Position = offset;
            if (this.trace)
            {
                Console.WriteLine("[sigrecord] offset=" + offset);
            }
            byte leading = this.reader.ReadByte8();
            Log("leadingByte", leading);
            return ReadSignature();
        }

        private MethodSignature ReadSignature()
        {
            MethodSignature sig = new MethodSignature();

            int paramCount = this.reader.ReadByte8();
            Log("paramCount", paramCount);
            for (int i = 0; i < paramCount; i++)
            {
                ParamEntry p = new ParamEntry();
                p.TypeId = this.reader.ReadInt32();
                p.ByRef = this.reader.ReadByte8() != 0;
                Log("param[" + i + "]", p.TypeId + (p.ByRef ? " byref" : ""));
                sig.Parameters.Add(p);
            }

            int genCount = this.reader.ReadByte8();
            Log("genCount", genCount);
            for (int i = 0; i < genCount; i++)
            {
                int id = this.reader.ReadInt32();
                Log("gen[" + i + "]", id);
                sig.GenericParameters.Add(id);
            }

            int localCount = this.reader.ReadByte8();
            Log("localCount", localCount);
            for (int i = 0; i < localCount; i++)
            {
                int id = this.reader.ReadInt32();
                Log("local[" + i + "]", id);
                sig.Locals.Add(id);
            }

            sig.ReturnTypeId = this.reader.ReadInt32();
            Log("returnTypeId", sig.ReturnTypeId);
            sig.Flags = this.reader.ReadByte8();
            Log("flags", sig.Flags);
            sig.DeclaringTypeId = this.reader.ReadInt32();
            Log("declaringTypeId", sig.DeclaringTypeId);
            sig.Name = this.reader.ReadString();
            Log("name", "\"" + sig.Name + "\"");

            return sig;
        }

        private List<EhClause> ReadHandlers()
        {
            List<EhClause> handlers = new List<EhClause>();
            int count = this.reader.ReadUInt16();
            Log("ehCount", count);
            for (int i = 0; i < count; i++)
            {
                EhClause c = new EhClause();
                c.ClauseType = this.reader.ReadByte8();
                c.CatchTypeId = this.reader.ReadInt32();
                c.HandlerStart = this.reader.ReadUInt32();
                c.TryStart = this.reader.ReadUInt32();
                c.TryLength = this.reader.ReadUInt32();
                c.Extra = this.reader.ReadUInt32();
                Log("eh[" + i + "]", c.ClauseType + " catchId=" + c.CatchTypeId +
                    " handlerStart=" + c.HandlerStart + " try=[" + c.TryStart + ",+" + c.TryLength + "] extra=" + c.Extra);
                handlers.Add(c);
            }
            return handlers;
        }

        public static void Dump(string storePath, long offset)
        {
            byte[] store = File.ReadAllBytes(storePath);
            StoreReader reader = MethodStore.OpenStore(store);
            MethodRecordReader recReader = new MethodRecordReader(reader, true);
            MethodRecord rec = recReader.Read(offset);
            Console.WriteLine("[parsed] params=" + rec.Signature.Parameters.Count +
                " gen=" + rec.Signature.GenericParameters.Count +
                " locals=" + rec.Signature.Locals.Count +
                " eh=" + rec.Handlers.Count +
                " codeLen=" + rec.Bytecode.Length);
            for (int i = 0; i < rec.Bytecode.Length; i += 16)
            {
                System.Text.StringBuilder hex = new System.Text.StringBuilder();
                for (int j = 0; j < 16 && i + j < rec.Bytecode.Length; j++)
                {
                    hex.Append(rec.Bytecode[i + j].ToString("X2")).Append(' ');
                }
                Console.WriteLine("  " + i.ToString("D4") + "  " + hex);
            }
        }
    }
}
