using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Protections
{
    static class ResourceDecrypt
    {
        public static int Run(ModuleDefMD module)
        {
            MethodDef packer = FindPacker(module);
            if (packer == null)
            {
                return 0;
            }
            RemoveCall(module, packer);
            return 1;
        }

        static MethodDef FindPacker(ModuleDefMD module)
        {
            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.HasBody || method.Body.Instructions.Count <= 100)
                    {
                        continue;
                    }
                    if (method.Body.Variables.Count != 13)
                    {
                        continue;
                    }
                    if (method.Body.Instructions[0].OpCode != OpCodes.Ldc_I4)
                    {
                        continue;
                    }
                    if (method.Body.Instructions[5].OpCode != OpCodes.Ldtoken)
                    {
                        continue;
                    }
                    if (method.Body.Instructions[6].OpCode != OpCodes.Call)
                    {
                        continue;
                    }
                    for (int i = 1; i < method.Body.Instructions.Count; i++)
                    {
                        Instruction instruction = method.Body.Instructions[i];
                        if (instruction.OpCode == OpCodes.Callvirt &&
                            instruction.Operand != null &&
                            instruction.Operand.ToString().Contains("AssemblyResolve") &&
                            method.Body.Instructions[i - 1].OpCode == OpCodes.Newobj)
                        {
                            return method;
                        }
                    }
                }
            }
            return null;
        }

        static void RemoveCall(ModuleDefMD module, MethodDef packer)
        {
            TypeDef globalType = module.GlobalType;
            if (globalType == null)
            {
                return;
            }
            MethodDef cctor = globalType.FindStaticConstructor();
            if (cctor == null || !cctor.HasBody)
            {
                return;
            }
            var instructions = cctor.Body.Instructions;
            for (int i = instructions.Count - 1; i >= 0; i--)
            {
                if (instructions[i].OpCode == OpCodes.Call &&
                    instructions[i].Operand is MethodDef called &&
                    called == packer)
                {
                    instructions[i].OpCode = OpCodes.Nop;
                    instructions[i].Operand = null;
                }
            }
        }
    }
}
