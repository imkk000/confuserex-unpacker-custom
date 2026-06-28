using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Protections.Devirt
{
    public sealed class CilEmitter
    {
        private readonly ModuleDefMD module;
        private readonly DnlibResolver resolver;

        public CilEmitter(ModuleDefMD module, DnlibResolver resolver)
        {
            this.module = module;
            this.resolver = resolver;
        }

        public CilBody Emit(MethodRecord record, MethodDef target)
        {
            List<VmInstruction> vmInstrs = BytecodeDecoder.Decode(record.Bytecode, OpcodeMap.KeyToOperandType());

            List<Local> locals = new List<Local>();
            foreach (int localTypeId in record.Signature.Locals)
            {
                locals.Add(new Local(this.resolver.ResolveTypeSig(localTypeId)));
            }

            List<Instruction> ordered = new List<Instruction>();
            Dictionary<int, Instruction> ipToInstr = new Dictionary<int, Instruction>();
            foreach (VmInstruction vm in vmInstrs)
            {
                Instruction instr = Translate(vm, target, locals);
                ipToInstr[vm.Ip] = instr;
                ordered.Add(instr);
            }

            for (int i = 0; i < vmInstrs.Count; i++)
            {
                FixBranches(vmInstrs[i], ordered[i], ipToInstr);
            }

            ApplyTerminalReturn(ordered, target);

            CilBody body = new CilBody();
            foreach (Local local in locals)
            {
                body.Variables.Add(local);
            }
            foreach (Instruction instr in ordered)
            {
                body.Instructions.Add(instr);
            }
            EmitHandlers(record, vmInstrs, ordered, ipToInstr, body);
            body.InitLocals = locals.Count > 0;
            body.UpdateInstructionOffsets();
            return body;
        }

        private Instruction Translate(VmInstruction vm, MethodDef target, List<Local> locals)
        {
            OpInfo info = OpcodeMap.ByKey[vm.Key];

            if (info.IsUnifiedCall)
            {
                return TranslateUnifiedCall(vm);
            }
            if (info.IsGenericStind)
            {
                throw new NotSupportedException("generic stind at ip " + vm.Ip + " needs stack-type analysis");
            }
            if (info.Cil.Code == Code.UNKNOWN1)
            {
                throw new NotSupportedException("unmapped handler " + info.Handler + " at ip " + vm.Ip);
            }

            switch (info.Cil.OperandType)
            {
                case dnlib.DotNet.Emit.OperandType.InlineNone:
                    return Instruction.Create(info.Cil);
                case dnlib.DotNet.Emit.OperandType.ShortInlineI:
                    return Instruction.Create(info.Cil, (sbyte)Convert.ToInt32(vm.Operand));
                case dnlib.DotNet.Emit.OperandType.InlineI:
                    return Instruction.Create(info.Cil, Convert.ToInt32(vm.Operand));
                case dnlib.DotNet.Emit.OperandType.InlineI8:
                    return Instruction.Create(info.Cil, Convert.ToInt64(vm.Operand));
                case dnlib.DotNet.Emit.OperandType.ShortInlineR:
                    return Instruction.Create(info.Cil, BitConverter.Int32BitsToSingle((int)(uint)vm.Operand));
                case dnlib.DotNet.Emit.OperandType.InlineR:
                    return Instruction.Create(info.Cil, Convert.ToDouble(vm.Operand));
                case dnlib.DotNet.Emit.OperandType.InlineString:
                    return Instruction.Create(info.Cil, this.resolver.ResolveString((int)vm.Operand));
                case dnlib.DotNet.Emit.OperandType.InlineType:
                    return Instruction.Create(info.Cil, this.resolver.ResolveTypeSig((int)vm.Operand).ToTypeDefOrRef());
                case dnlib.DotNet.Emit.OperandType.InlineField:
                    return Instruction.Create(info.Cil, this.resolver.ResolveField((int)vm.Operand));
                case dnlib.DotNet.Emit.OperandType.InlineTok:
                    return Instruction.Create(info.Cil, this.resolver.ResolveTokenOperand((int)vm.Operand));
                case dnlib.DotNet.Emit.OperandType.InlineMethod:
                    return TranslateMethod(info, (int)vm.Operand);
                case dnlib.DotNet.Emit.OperandType.InlineVar:
                case dnlib.DotNet.Emit.OperandType.ShortInlineVar:
                    return TranslateVar(info, Convert.ToInt32(vm.Operand), target, locals);
                case dnlib.DotNet.Emit.OperandType.InlineBrTarget:
                case dnlib.DotNet.Emit.OperandType.ShortInlineBrTarget:
                    return Instruction.Create(info.Cil, Instruction.Create(OpCodes.Nop));
                case dnlib.DotNet.Emit.OperandType.InlineSwitch:
                    return Instruction.Create(info.Cil, new Instruction[0]);
                case dnlib.DotNet.Emit.OperandType.InlineSig:
                    throw new NotSupportedException("calli (InlineSig) at ip " + vm.Ip + " not supported");
                default:
                    throw new NotSupportedException("operand type " + info.Cil.OperandType + " at ip " + vm.Ip);
            }
        }

        private Instruction TranslateMethod(OpInfo info, int id)
        {
            IMethod method = this.resolver.ResolveMethod(id);
            if (info.Cil.Code != Code.Call && info.Cil.Code != Code.Callvirt)
            {
                return Instruction.Create(info.Cil, method);
            }
            if (method.Name == ".ctor")
            {
                return Instruction.Create(OpCodes.Newobj, method);
            }
            OpCode op = HasThis(method) ? info.Cil : OpCodes.Call;
            return Instruction.Create(op, method);
        }

        private Instruction TranslateUnifiedCall(VmInstruction vm)
        {
            int op = (int)vm.Operand;
            bool isMethodCall = (op & int.MinValue) != 0;
            bool isVirtual = (op & 0x40000000) != 0;
            int id = op & 0x3FFFFFFF;
            if (!isMethodCall)
            {
                throw new NotSupportedException("unified calli at ip " + vm.Ip + " not supported");
            }
            IMethod method = this.resolver.ResolveSignatureMethod(id);
            if (method.Name == ".ctor")
            {
                return Instruction.Create(OpCodes.Newobj, method);
            }
            OpCode chosen = HasThis(method) && isVirtual ? OpCodes.Callvirt : OpCodes.Call;
            return Instruction.Create(chosen, method);
        }

        private static bool HasThis(IMethod method)
        {
            return method.MethodSig != null && method.MethodSig.HasThis;
        }

        private static Instruction TranslateVar(OpInfo info, int index, MethodDef target, List<Local> locals)
        {
            if (IsArgOpcode(info.Cil.Code))
            {
                return Instruction.Create(info.Cil, target.Parameters[index]);
            }
            return Instruction.Create(info.Cil, locals[index]);
        }

        private static bool IsArgOpcode(Code code)
        {
            switch (code)
            {
                case Code.Ldarg:
                case Code.Ldarg_S:
                case Code.Ldarga:
                case Code.Ldarga_S:
                case Code.Starg:
                case Code.Starg_S:
                    return true;
                default:
                    return false;
            }
        }

        private static void FixBranches(VmInstruction vm, Instruction instr, Dictionary<int, Instruction> ipToInstr)
        {
            OpInfo info = OpcodeMap.ByKey[vm.Key];
            if (info.IsUnifiedCall || info.Cil.Code == Code.UNKNOWN1)
            {
                return;
            }
            if (info.Cil.OperandType == dnlib.DotNet.Emit.OperandType.InlineBrTarget ||
                info.Cil.OperandType == dnlib.DotNet.Emit.OperandType.ShortInlineBrTarget)
            {
                instr.Operand = ResolveTarget(ipToInstr, (uint)vm.Operand);
            }
            else if (info.Cil.OperandType == dnlib.DotNet.Emit.OperandType.InlineSwitch)
            {
                uint[] offsets = (uint[])vm.Operand;
                Instruction[] targets = new Instruction[offsets.Length];
                for (int i = 0; i < offsets.Length; i++)
                {
                    targets[i] = ResolveTarget(ipToInstr, offsets[i]);
                }
                instr.Operand = targets;
            }
        }

        private static Instruction ResolveTarget(Dictionary<int, Instruction> ipToInstr, uint offset)
        {
            if (!ipToInstr.TryGetValue((int)offset, out Instruction target))
            {
                throw new InvalidOperationException("branch target ip " + offset + " does not align with an instruction");
            }
            return target;
        }

        private static void ApplyTerminalReturn(List<Instruction> ordered, MethodDef target)
        {
            bool returnsVoid = target.ReturnType.RemovePinnedAndModifiers().ElementType == ElementType.Void;
            if (ordered.Count == 0)
            {
                ordered.Add(Instruction.Create(OpCodes.Ret));
                return;
            }

            Instruction last = ordered[ordered.Count - 1];
            if (last.OpCode.Code == Code.Ret)
            {
                return;
            }
            if (!returnsVoid && last.OpCode.Code == Code.Pop)
            {
                last.OpCode = OpCodes.Ret;
                last.Operand = null;
                return;
            }
            if (returnsVoid)
            {
                ordered.Add(Instruction.Create(OpCodes.Ret));
            }
        }

        private void EmitHandlers(MethodRecord record, List<VmInstruction> vmInstrs, List<Instruction> ordered, Dictionary<int, Instruction> ipToInstr, CilBody body)
        {
            foreach (EhClause clause in record.Handlers)
            {
                ExceptionHandler handler = new ExceptionHandler((ExceptionHandlerType)clause.ClauseType);
                handler.TryStart = ResolveTarget(ipToInstr, clause.TryStart);
                uint tryEndIp = clause.TryStart + clause.TryLength;
                handler.TryEnd = FirstInstrAfter(vmInstrs, ordered, tryEndIp);
                handler.HandlerStart = ResolveTarget(ipToInstr, clause.HandlerStart);

                if (clause.ClauseType == 2 || clause.ClauseType == 4)
                {
                    handler.HandlerEnd = EndScopeHandler(vmInstrs, ordered, clause.HandlerStart);
                }
                else if (clause.ClauseType == 0)
                {
                    handler.CatchType = this.resolver.ResolveTypeSig(clause.CatchTypeId).ToTypeDefOrRef();
                    handler.HandlerEnd = EndCatchHandler(vmInstrs, ordered, ipToInstr, clause.HandlerStart, tryEndIp);
                }
                else if (clause.ClauseType == 1)
                {
                    handler.FilterStart = ResolveTarget(ipToInstr, clause.Extra);
                    handler.HandlerEnd = EndCatchHandler(vmInstrs, ordered, ipToInstr, clause.HandlerStart, tryEndIp);
                }
                body.ExceptionHandlers.Add(handler);
            }
        }

        private static Instruction EndScopeHandler(List<VmInstruction> vmInstrs, List<Instruction> ordered, uint handlerStart)
        {
            for (int i = 0; i < vmInstrs.Count; i++)
            {
                if ((uint)vmInstrs[i].Ip >= handlerStart && ordered[i].OpCode.Code == Code.Ret)
                {
                    ordered[i].OpCode = OpCodes.Endfinally;
                    ordered[i].Operand = null;
                    if (i + 1 < ordered.Count)
                    {
                        return ordered[i + 1];
                    }
                    throw new InvalidOperationException("endfinally at end of method has no following instruction for handler end");
                }
            }
            throw new InvalidOperationException("no scope-return found for finally/fault handler at ip " + handlerStart);
        }

        private static Instruction EndCatchHandler(List<VmInstruction> vmInstrs, List<Instruction> ordered, Dictionary<int, Instruction> ipToInstr, uint handlerStart, uint tryEndIp)
        {
            Instruction tryTerminal = ipToInstr.TryGetValue((int)tryEndIp, out Instruction t) ? t : null;
            if (tryTerminal != null &&
                (tryTerminal.OpCode.Code == Code.Leave || tryTerminal.OpCode.Code == Code.Leave_S) &&
                tryTerminal.Operand is Instruction leaveTarget)
            {
                return leaveTarget;
            }
            return FirstInstrAfter(vmInstrs, ordered, handlerStart);
        }

        private static Instruction FirstInstrAfter(List<VmInstruction> vmInstrs, List<Instruction> ordered, uint ip)
        {
            for (int i = 0; i < vmInstrs.Count; i++)
            {
                if (vmInstrs[i].Ip > ip)
                {
                    return ordered[i];
                }
            }
            throw new InvalidOperationException("no instruction after ip " + ip + " for exception-region end");
        }
    }
}
