using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Protections
{
    public class ProxyCalls
    {
        public static List<MethodDef> junkMethods = new List<MethodDef>();
        public static bool _RemoveJunkMethods = false;

        public static int Execute()
        {
            int num = 0;
            foreach (TypeDef typeDef in ConfuserexUnpacker.Program.MainModule.GetTypes())
            {
                foreach (MethodDef methodDef in typeDef.Methods)
                {
                    if (methodDef.HasBody)
                    {
                        IList<Instruction> instructions = methodDef.Body.Instructions;
                        int num2;
                        for (int i = 0; i < instructions.Count; i = num2 + 1)
                        {
                            if (instructions[i].OpCode.Equals(OpCodes.Call))
                            {
                                try
                                {
                                    MethodDef methodDef2 = instructions[i].Operand as MethodDef;
                                    if (!(methodDef2 == null))
                                    {
                                        if (!(!methodDef2.IsStatic || !typeDef.Methods.Contains(methodDef2)))
                                        {
                                            OpCode opCode;
                                            object proxyValues = ProxyCalls.GetProxyValues(methodDef2, out opCode);
                                            if (!(opCode == null || proxyValues == null))
                                            {
                                                instructions[i].OpCode = opCode;
                                                instructions[i].Operand = proxyValues;
                                                num2 = num;
                                                num = num2 + 1;
                                                if (!ProxyCalls.junkMethods.Contains(methodDef2))
                                                {
                                                    ProxyCalls.junkMethods.Add(methodDef2);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                }
                            }
                            num2 = i;
                        }
                    }
                }
            }
            return num;
        }

        private static object GetProxyValues(MethodDef method, out OpCode opCode)
        {
            Instruction[] array = method.Body.Instructions.ToArray<Instruction>();
            int num = array.Length - 2;
            opCode = null;
            bool flag = !array[num].OpCode.Equals(OpCodes.Newobj) && !array[num].OpCode.Equals(OpCodes.Call) && !array[num].OpCode.Equals(OpCodes.Callvirt);
            object result;
            if (flag)
            {
                result = null;
            }
            else
            {
                bool flag2 = array[num + 1].OpCode.Code != Code.Ret;
                if (flag2)
                {
                    result = null;
                }
                else
                {
                    bool flag3 = array.Length != method.Parameters.Count + 2;
                    if (flag3)
                    {
                        result = null;
                    }
                    else
                    {
                        opCode = array[num].OpCode;
                        result = array[num].Operand;
                    }
                }
            }
            return result;
        }

        private static void RemoveJunkMethods()
        {
            int num = 0;
            foreach (TypeDef typeDef in ConfuserexUnpacker.Program.MainModule.GetTypes())
            {
                List<MethodDef> list = new List<MethodDef>();
                foreach (MethodDef item in typeDef.Methods)
                {
                    bool flag = ProxyCalls.junkMethods.Contains(item);
                    if (flag)
                    {
                        list.Add(item);
                    }
                }
                int num2;
                for (int i = 0; i < list.Count; i = num2 + 1)
                {
                    typeDef.Methods.Remove(list[i]);
                    num2 = num;
                    num = num2 + 1;
                    num2 = i;
                }
                list.Clear();
            }
            ProxyCalls.junkMethods.Clear();
        }
    }
}
