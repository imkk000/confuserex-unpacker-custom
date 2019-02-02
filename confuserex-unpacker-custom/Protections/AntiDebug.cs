using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Protections
{
    class AntiDebug
    {
        public static void Run(ModuleDefMD module)
        {
            foreach (TypeDef current in module.Types)
            {
                if (current.Name.Equals("<Module>"))
                {
                    foreach (MethodDef current2 in current.Methods)
                    {
                        if (current2.IsConstructor)
                        {
                            for (int i = 0; i < current2.Body.Instructions.Count; i++)
                            {
                                if (current2.Body.Instructions[i].OpCode == OpCodes.Call)
                                {
                                    current2.Body.Instructions[i].OpCode = OpCodes.Nop;
                                    current2.Body.Instructions[i].Operand = null;
                                }
                            }
                        }
                    }
                }
            }
            for (int i = 0; i < module.CustomAttributes.Count; i++)
            {
                module.CustomAttributes.RemoveAt(i);
            }
        }
    }
}
