using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Protections
{
    class Packer
    {
        public static bool IsPacked(ModuleDefMD module)
        {
            
            // Thanks to 0xd4d https://github.com/0xd4d/dnlib/issues/72
            for (uint rid = 1; rid <= module.MetaData.TablesStream.FileTable.Rows; rid++)
            {
                dnlib.DotNet.MD.RawFileRow row = new dnlib.DotNet.MD.RawFileRow(); 
                row =module.TablesStream.ReadFileRow(rid);
                string name = module.StringsStream.ReadNoNull(row.Name);
                if (name!="koi") continue;
                

                return true;
            }

            return false;
        }
        private static byte[] initialValue;
        public static int epToken;

        private static void arrayFinder(Local loc)
        {
            MethodDef entryPoint = ConfuserexUnpacker.Program.module.EntryPoint;
            for (int i = 0; i < entryPoint.Body.Instructions.Count; i++)
            {
                if (entryPoint.Body.Instructions[i].IsStloc())
                {
                    if (entryPoint.Body.Instructions[i].GetLocal(entryPoint.Body.Variables) == loc)
                    {
                        if(entryPoint.Body.Instructions[i-1].OpCode == OpCodes.Call&&entryPoint.Body.Instructions[i-2].OpCode == OpCodes.Ldtoken)
                        {
                            var tester = entryPoint.Body.Instructions[i - 2].Operand as FieldDef;
                            initialValue = tester.InitialValue;
                            break;
                        }
                    }
                }
            }
        }
        public static void findLocal()
        {
            var manifestModule = ConfuserexUnpacker.Program.asm.ManifestModule;
            MethodDef entryPoint = ConfuserexUnpacker.Program.module.EntryPoint;
            var aaa = ConfuserexUnpacker.Program.module.CorLibTypes.GetTypeRef("System.Runtime.InteropServices", "GCHandle");
            var tester = ConfuserexUnpacker.Program.module.EntryPoint.Body.Variables.Where(i=>i.Type.Namespace == "System.Runtime.InteropServices" && i.Type.TypeName == "GCHandle").ToArray();
            if(tester.Length != 0)
            {
                Local loc = tester[0];
                for(int i = 0; i < entryPoint.Body.Instructions.Count; i++)
                {
                    if (entryPoint.Body.Instructions[i].IsStloc())
                    {
                        if (entryPoint.Body.Instructions[i].GetLocal(entryPoint.Body.Variables) == loc)
                        {
                            if(entryPoint.Body.Instructions[i-1].OpCode == OpCodes.Call)
                            {
                                if (entryPoint.Body.Instructions[i - 2].IsLdcI4())
                                {
                                    if(entryPoint.Body.Instructions[i - 3].IsLdloc())
                                    {
                                        MethodDef decryptMethod = entryPoint.Body.Instructions[i - 1].Operand as MethodDef;
                                        var dec = manifestModule.ResolveMethod(decryptMethod.MDToken.ToInt32());
                                        object[] param = new object[2];
                                        param[1] = (uint)entryPoint.Body.Instructions[i - 2].GetLdcI4Value();
                                        Local loc2 = entryPoint.Body.Instructions[i - 3].GetLocal(entryPoint.Body.Variables);
                                        arrayFinder(loc2);
                                        uint[] decoded = new uint[initialValue.Length / 4];
                                        Buffer.BlockCopy(initialValue, 0, decoded, 0, initialValue.Length);
                                        param[0] = decoded;
                                        GCHandle aaaaa = (GCHandle)dec.Invoke(null, param);
                                        ConfuserexUnpacker.Program.module = ModuleDefMD.Load((byte[])aaaaa.Target);
                                        var key = manifestModule.ResolveSignature(0x11000001);
                                      
                                        epToken= ((int)key[0] | (int)key[1] << 8 | (int)key[2] << 16 | (int)key[3] << 24);
                                        ConfuserexUnpacker.Program.module.EntryPoint = ConfuserexUnpacker.Program.module.ResolveToken(epToken) as MethodDef;
                                        ConfuserexUnpacker.Program.asm = Assembly.Load((byte[])aaaaa.Target);
                                        
                                        return;
                                    }
                                  

                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
