using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Protections.Devirt
{
    public enum RefCategory
    {
        Method = 0,
        Field = 1,
        Type = 2,
        Calli = 3,
        String = 4,
        RawToken = 100,
    }

    public sealed class TypeRef
    {
        public string Name;
        public bool IsGenericInstance;
        public bool IsGenericParam;
        public int MethodGenericIndex;
        public int TypeGenericIndex;
        public List<long> GenericArgumentIds = new List<long>();
    }

    public sealed class MethodRef
    {
        public long DeclaringTypeId;
        public byte Flags;
        public string Name;
        public long ReturnTypeId;
        public List<long> ParameterTypeIds = new List<long>();
        public List<long> GenericArgumentIds = new List<long>();

        public bool IsStatic => (Flags & 1) != 0;
        public bool IsGenericInstance => (Flags & 2) != 0;
        public bool IsConstructor => Name == ".ctor" || Name == ".cctor";
    }

    public sealed class FieldRef
    {
        public long DeclaringTypeId;
        public string Name;
        public bool IsStatic;
    }

    public sealed class CalliRef
    {
        public int A;
        public int B;
    }

    public sealed class ReferenceRecord
    {
        public long Id;
        public RefCategory Category;
        public int RawToken;
        public TypeRef Type;
        public MethodRef Method;
        public FieldRef Field;
        public CalliRef Calli;
        public string Literal;
    }

    public sealed class RefRecordReader
    {
        private readonly StoreReader reader;

        public RefRecordReader(StoreReader reader)
        {
            this.reader = reader;
        }

        public static RefRecordReader FromStore(byte[] decryptedStore)
        {
            return new RefRecordReader(MethodStore.OpenStore(decryptedStore));
        }

        public ReferenceRecord Read(long id)
        {
            this.reader.Position = id;
            ReferenceRecord record = new ReferenceRecord { Id = id };

            byte kind = this.reader.ReadByte8();
            if (kind == 0)
            {
                record.Category = RefCategory.RawToken;
                record.RawToken = this.reader.ReadInt32();
                return record;
            }

            byte category = this.reader.ReadByte8();
            record.Category = (RefCategory)category;
            switch (record.Category)
            {
                case RefCategory.Method:
                    record.Method = ReadMethod();
                    break;
                case RefCategory.Field:
                    record.Field = ReadField();
                    break;
                case RefCategory.Type:
                    record.Type = ReadType();
                    break;
                case RefCategory.Calli:
                    record.Calli = ReadCalli();
                    break;
                case RefCategory.String:
                    record.Literal = this.reader.ReadString();
                    break;
                default:
                    throw new InvalidOperationException("unknown reference category " + category + " at id " + id);
            }
            return record;
        }

        private long ReadTypeRefId()
        {
            return this.reader.ReadInt32();
        }

        private MethodRef ReadMethod()
        {
            MethodRef m = new MethodRef();
            m.DeclaringTypeId = ReadTypeRefId();
            m.Flags = this.reader.ReadByte8();
            m.Name = this.reader.ReadString();
            m.ReturnTypeId = ReadTypeRefId();

            int paramCount = this.reader.Read7BitEncodedInt();
            for (int i = 0; i < paramCount; i++)
            {
                m.ParameterTypeIds.Add(ReadTypeRefId());
            }

            int genericCount = this.reader.Read7BitEncodedInt();
            for (int i = 0; i < genericCount; i++)
            {
                m.GenericArgumentIds.Add(ReadTypeRefId());
            }
            return m;
        }

        private FieldRef ReadField()
        {
            FieldRef f = new FieldRef();
            f.DeclaringTypeId = ReadTypeRefId();
            f.Name = this.reader.ReadString();
            f.IsStatic = this.reader.ReadByte8() != 0;
            return f;
        }

        private TypeRef ReadType()
        {
            TypeRef t = new TypeRef();
            t.Name = this.reader.ReadString();
            t.IsGenericInstance = this.reader.ReadByte8() != 0;
            t.IsGenericParam = this.reader.ReadByte8() != 0;
            t.MethodGenericIndex = this.reader.ReadInt32();
            t.TypeGenericIndex = this.reader.ReadInt32();
            if (t.IsGenericInstance)
            {
                int argCount = this.reader.Read7BitEncodedInt();
                for (int i = 0; i < argCount; i++)
                {
                    t.GenericArgumentIds.Add(ReadTypeRefId());
                }
            }
            return t;
        }

        private CalliRef ReadCalli()
        {
            CalliRef c = new CalliRef();
            c.A = this.reader.ReadInt32();
            c.B = this.reader.ReadInt32();
            return c;
        }
    }

    public sealed class RefDescriber
    {
        private readonly RefRecordReader reader;
        private readonly MethodRecordReader methodReader;
        private readonly Dictionary<long, string> typeNameCache = new Dictionary<long, string>();

        public RefDescriber(byte[] decryptedStore)
        {
            this.reader = new RefRecordReader(MethodStore.OpenStore(decryptedStore));
            this.methodReader = new MethodRecordReader(MethodStore.OpenStore(decryptedStore), false);
        }

        public string DescribeSignatureMethod(long id)
        {
            MethodSignature sig;
            try
            {
                sig = this.methodReader.ReadSignatureRecord(id);
            }
            catch (Exception ex)
            {
                return "<sig-error " + ex.GetType().Name + ": " + ex.Message + ">";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(DescribeTypeId(sig.ReturnTypeId)).Append(' ');
            sb.Append(DescribeTypeId(sig.DeclaringTypeId)).Append("::").Append(sig.Name).Append('(');
            for (int i = 0; i < sig.Parameters.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                ParamEntry p = sig.Parameters[i];
                sb.Append(DescribeTypeId(p.TypeId));
                if (p.ByRef)
                {
                    sb.Append('&');
                }
            }
            sb.Append(')');
            return sb.ToString();
        }

        public string Describe(long id)
        {
            ReferenceRecord record;
            try
            {
                record = this.reader.Read(id);
            }
            catch (Exception ex)
            {
                return "<error " + ex.GetType().Name + ": " + ex.Message + ">";
            }

            switch (record.Category)
            {
                case RefCategory.RawToken:
                    return "token 0x" + record.RawToken.ToString("X8");
                case RefCategory.String:
                    return "\"" + record.Literal + "\"";
                case RefCategory.Type:
                    return DescribeType(record.Type);
                case RefCategory.Field:
                    return DescribeTypeId(record.Field.DeclaringTypeId) + "::" + record.Field.Name +
                        (record.Field.IsStatic ? " static" : "");
                case RefCategory.Method:
                    return DescribeMethod(record.Method);
                case RefCategory.Calli:
                    return "calli(" + record.Calli.A + ", " + record.Calli.B + ")";
                default:
                    return "<unknown>";
            }
        }

        private string DescribeMethod(MethodRef m)
        {
            StringBuilder sb = new StringBuilder();
            if (m.IsStatic)
            {
                sb.Append("static ");
            }
            sb.Append(DescribeTypeId(m.ReturnTypeId)).Append(' ');
            sb.Append(DescribeTypeId(m.DeclaringTypeId)).Append("::").Append(m.Name).Append('(');
            for (int i = 0; i < m.ParameterTypeIds.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(DescribeTypeId(m.ParameterTypeIds[i]));
            }
            sb.Append(')');
            return sb.ToString();
        }

        private string DescribeTypeId(long id)
        {
            if (this.typeNameCache.TryGetValue(id, out string cached))
            {
                return cached;
            }
            string name;
            try
            {
                ReferenceRecord record = this.reader.Read(id);
                name = record.Category == RefCategory.Type
                    ? DescribeType(record.Type)
                    : "<type@" + id + " cat=" + record.Category + ">";
            }
            catch (Exception ex)
            {
                name = "<type@" + id + " " + ex.GetType().Name + ">";
            }
            this.typeNameCache[id] = name;
            return name;
        }

        private string DescribeType(TypeRef t)
        {
            if (t.IsGenericParam)
            {
                return t.MethodGenericIndex != -1
                    ? "!!" + t.MethodGenericIndex
                    : "!" + t.TypeGenericIndex;
            }
            int comma = t.Name.IndexOf(',');
            string shortName = comma >= 0 ? t.Name.Substring(0, comma) : t.Name;
            return shortName;
        }
    }

    public static class RefResolveCli
    {
        public static void Dump(string storePath, long methodOffset)
        {
            byte[] store = File.ReadAllBytes(storePath);
            StoreReader reader = MethodStore.OpenStore(store);
            MethodRecord rec = new MethodRecordReader(reader, false).Read(methodOffset);
            List<VmInstruction> instrs = BytecodeDecoder.Decode(rec.Bytecode, OpcodeMap.KeyToOperandType());
            RefDescriber describer = new RefDescriber(store);

            Console.WriteLine("[resolve] method@" + methodOffset + " operands:");
            foreach (VmInstruction instr in instrs)
            {
                OpInfo info = OpcodeMap.ByKey[instr.Key];
                if (!TryGetOperandId(instr, info, out long id, out bool isVirtual, out bool isMethodCall))
                {
                    continue;
                }
                string mnem = info.IsUnifiedCall ? "call*" : info.Cil.Name;
                string note = info.IsUnifiedCall
                    ? " [" + (isMethodCall ? "method" : "calli") + (isVirtual ? " virtual" : "") + "]"
                    : "";
                string described = info.IsUnifiedCall && isMethodCall
                    ? describer.DescribeSignatureMethod(id)
                    : describer.Describe(id);
                Console.WriteLine("IL_" + instr.Ip.ToString("X4") + "  " + mnem.PadRight(14) +
                    " id=" + id + note + "  => " + described);
            }
        }

        public static bool TryGetOperandId(VmInstruction instr, OpInfo info, out long id, out bool isVirtual, out bool isMethodCall)
        {
            id = 0;
            isVirtual = false;
            isMethodCall = false;
            if (info.OperandType == 2)
            {
                id = (int)instr.Operand;
                return true;
            }
            if (info.OperandType == 6 && info.IsUnifiedCall)
            {
                int op = (int)instr.Operand;
                isMethodCall = (op & int.MinValue) != 0;
                isVirtual = (op & 0x40000000) != 0;
                id = op & 0x3FFFFFFF;
                return true;
            }
            return false;
        }
    }
}
