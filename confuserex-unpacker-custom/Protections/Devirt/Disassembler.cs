using System;
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet.Emit;

namespace Protections.Devirt
{
    public static class Disassembler
    {
        public static void Dump(string storePath, long offset)
        {
            byte[] store = File.ReadAllBytes(storePath);
            StoreReader reader = MethodStore.OpenStore(store);
            MethodRecordReader recReader = new MethodRecordReader(reader, false);
            MethodRecord rec = recReader.Read(offset);

            Console.WriteLine("[method] params=" + rec.Signature.Parameters.Count +
                " locals=" + rec.Signature.Locals.Count +
                " ret=" + rec.Signature.ReturnTypeId +
                " decl=" + rec.Signature.DeclaringTypeId +
                " codeLen=" + rec.Bytecode.Length);

            List<VmInstruction> instrs = BytecodeDecoder.Decode(rec.Bytecode, OpcodeMap.KeyToOperandType());
            foreach (VmInstruction instr in instrs)
            {
                OpInfo info = OpcodeMap.ByKey[instr.Key];
                string mnem;
                if (info.IsUnifiedCall)
                {
                    mnem = "call/callvirt*";
                }
                else if (info.IsGenericStind)
                {
                    mnem = "stind.*";
                }
                else if (info.Cil.Code == Code.UNKNOWN1)
                {
                    mnem = "??(" + info.Handler + ")";
                }
                else
                {
                    mnem = info.Cil.Name;
                }

                Console.WriteLine("IL_" + instr.Ip.ToString("X4") + "  " +
                    mnem.PadRight(16) + " " + FormatOperand(instr, info));
            }
        }

        private static string FormatOperand(VmInstruction instr, OpInfo info)
        {
            if (instr.Operand == null)
            {
                return "";
            }
            if (instr.Operand is uint[] targets)
            {
                return "[" + string.Join(", ", Array.ConvertAll(targets, t => "IL_" + t.ToString("X4"))) + "]";
            }
            if (info.OperandType == 1)
            {
                return "IL_" + ((uint)instr.Operand).ToString("X4");
            }
            return instr.Operand.ToString();
        }
    }
}
