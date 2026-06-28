using System;
using System.Collections.Generic;
using System.IO;

namespace Protections.Devirt
{
    public sealed class VmInstruction
    {
        public int Ip;
        public int Key;
        public byte OperandType;
        public object Operand;
    }

    public static class BytecodeDecoder
    {
        public static List<VmInstruction> Decode(byte[] code, IReadOnlyDictionary<int, byte> keyToOperandType)
        {
            List<VmInstruction> result = new List<VmInstruction>();
            StoreReader reader = new StoreReader(new MemoryStream(code));
            int ip = 0;
            while (ip < code.Length)
            {
                int key = reader.ReadInt32();
                ip += 4;
                if (!keyToOperandType.TryGetValue(key, out byte operandType))
                {
                    throw new InvalidOperationException("unknown opcode key 0x" + key.ToString("X8") + " at ip " + (ip - 4));
                }
                VmInstruction instr = new VmInstruction { Ip = ip - 4, Key = key, OperandType = operandType };
                instr.Operand = ReadOperand(reader, operandType, ref ip);
                result.Add(instr);
            }
            return result;
        }

        private static object ReadOperand(StoreReader reader, byte operandType, ref int ip)
        {
            switch (operandType)
            {
                case 11:
                    return null;
                case 0:
                    ip += 1;
                    return (sbyte)reader.ReadByte8();
                case 3:
                case 7:
                    ip += 1;
                    return (int)reader.ReadByte8();
                case 5:
                case 12:
                    ip += 2;
                    return (int)ReadUInt16(reader);
                case 1:
                    ip += 4;
                    return reader.ReadUInt32();
                case 2:
                case 6:
                    ip += 4;
                    return reader.ReadInt32();
                case 4:
                    ip += 4;
                    return reader.ReadUInt32();
                case 8:
                    ip += 8;
                    return reader.ReadDouble();
                case 10:
                    ip += 8;
                    return reader.ReadInt64();
                case 9:
                {
                    int count = reader.ReadInt32();
                    uint[] targets = new uint[count];
                    for (int i = 0; i < count; i++)
                    {
                        targets[i] = (uint)reader.ReadInt32();
                    }
                    ip += (count + 1) * 4;
                    return targets;
                }
                default:
                    throw new InvalidOperationException("unknown operand type " + operandType);
            }
        }

        private static ushort ReadUInt16(StoreReader reader)
        {
            byte a = reader.ReadByte8();
            byte b = reader.ReadByte8();
            return (ushort)(b | (a << 8));
        }
    }
}
