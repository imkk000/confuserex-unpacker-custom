using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Protections.Devirt;

namespace Protections
{
    static class StaticStrings
    {
        public static int Run(ModuleDefMD module)
        {
            byte[] pool = ConstPool.FindConstResource(module);
            byte[] key128 = ConstPool.FindKey128(module);
            byte[] store = StoreCipher.FindResource(module, StoreCipher.DefaultResourceName);
            if (pool == null || key128 == null || store == null)
            {
                return 0;
            }

            int num7 = ConstPool.RecoverNum7(pool, key128, store);
            MethodDef decryptor = FindDecryptor(module);
            if (decryptor == null)
            {
                return 0;
            }

            int decrypted = 0;
            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }
                    var instructions = method.Body.Instructions;
                    for (int i = 1; i < instructions.Count; i++)
                    {
                        if (instructions[i].OpCode != OpCodes.Call)
                        {
                            continue;
                        }
                        if (!(instructions[i].Operand is MethodDef called) || called != decryptor)
                        {
                            continue;
                        }
                        if (!instructions[i - 1].IsLdcI4())
                        {
                            continue;
                        }
                        int id = instructions[i - 1].GetLdcI4Value();
                        string value = ConstPool.DecodeString(pool, id, num7);
                        if (value == null)
                        {
                            continue;
                        }
                        instructions[i - 1].OpCode = OpCodes.Ldstr;
                        instructions[i - 1].Operand = value;
                        instructions[i].OpCode = OpCodes.Nop;
                        instructions[i].Operand = null;
                        decrypted++;
                    }
                }
            }
            return decrypted;
        }

        static MethodDef FindDecryptor(ModuleDefMD module)
        {
            foreach (TypeDef type in module.GetTypes())
            {
                if (!HasIntStringCache(type))
                {
                    continue;
                }
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.IsStatic || method.MethodSig == null)
                    {
                        continue;
                    }
                    if (method.MethodSig.Params.Count != 1)
                    {
                        continue;
                    }
                    if (method.MethodSig.Params[0].ElementType != ElementType.I4)
                    {
                        continue;
                    }
                    if (method.MethodSig.RetType.ElementType != ElementType.String)
                    {
                        continue;
                    }
                    return method;
                }
            }
            return null;
        }

        static bool HasIntStringCache(TypeDef type)
        {
            foreach (FieldDef field in type.Fields)
            {
                if (!(field.FieldType is GenericInstSig generic))
                {
                    continue;
                }
                if (generic.GenericArguments.Count != 2)
                {
                    continue;
                }
                if (!generic.GenericType.TypeName.Contains("ConcurrentDictionary"))
                {
                    continue;
                }
                if (generic.GenericArguments[0].ElementType == ElementType.I4 &&
                    generic.GenericArguments[1].ElementType == ElementType.String)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
