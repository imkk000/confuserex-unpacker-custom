using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace Protections.Devirt
{
    public sealed class StubSite
    {
        public MethodDef Method;
        public string Key;
    }

    public static class StubDetector
    {
        public static MethodDef FindInterpreter(ModuleDefMD module)
        {
            List<MethodDef> matches = new List<MethodDef>();
            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef m in type.Methods)
                {
                    if (m.MethodSig == null || m.ReturnType.FullName != "System.Object")
                    {
                        continue;
                    }
                    if (m.MethodSig.Params.Count != 3)
                    {
                        continue;
                    }
                    if (m.MethodSig.Params[0].FullName != "System.IO.Stream" ||
                        m.MethodSig.Params[1].FullName != "System.String" ||
                        m.MethodSig.Params[2].FullName != "System.Object[]")
                    {
                        continue;
                    }
                    matches.Add(m);
                }
            }
            if (matches.Count != 1)
            {
                throw new InvalidOperationException("expected exactly one interpreter method, found " + matches.Count);
            }
            return matches[0];
        }

        public static List<StubSite> FindStubs(ModuleDefMD module, MethodDef interpreter)
        {
            List<StubSite> stubs = new List<StubSite>();
            uint interpreterToken = interpreter.MDToken.Raw;
            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef m in type.Methods)
                {
                    if (m == interpreter || !m.HasBody)
                    {
                        continue;
                    }
                    string key = ExtractKey(m, interpreterToken);
                    if (key != null)
                    {
                        stubs.Add(new StubSite { Method = m, Key = key });
                    }
                }
            }
            return stubs;
        }

        private static string ExtractKey(MethodDef m, uint interpreterToken)
        {
            IList<Instruction> instrs = m.Body.Instructions;
            for (int i = 0; i < instrs.Count; i++)
            {
                Instruction instr = instrs[i];
                if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
                {
                    continue;
                }
                if (!(instr.Operand is IMethod callee) || callee.MDToken.Raw != interpreterToken)
                {
                    continue;
                }
                for (int j = i - 1; j >= 0; j--)
                {
                    if (instrs[j].OpCode.Code == Code.Ldstr && instrs[j].Operand is string s)
                    {
                        return s;
                    }
                }
            }
            return null;
        }
    }

    public sealed class DevirtResult
    {
        public int Ok;
        public int Total;
        public MethodDef Interpreter;
        public Dictionary<string, int> Failures = new Dictionary<string, int>();
        public List<string> Failed = new List<string>();
    }

    public static class DevirtAllCli
    {
        public static DevirtResult Sweep(ModuleDefMD module, byte[] store, HashSet<uint> onlyTokens = null)
        {
            DevirtResult result = new DevirtResult();
            result.Interpreter = StubDetector.FindInterpreter(module);
            List<StubSite> stubs = StubDetector.FindStubs(module, result.Interpreter);
            if (onlyTokens != null)
            {
                stubs = stubs.Where(s => onlyTokens.Contains(s.Method.MDToken.Raw)).ToList();
            }
            result.Total = stubs.Count;
            foreach (StubSite stub in stubs)
            {
                try
                {
                    long offset = MethodStore.KeyToOffset(stub.Key);
                    MethodRecord record = new MethodRecordReader(MethodStore.OpenStore(store), false).Read(offset);
                    DnlibResolver resolver = new DnlibResolver(module, store);
                    CilEmitter emitter = new CilEmitter(module, resolver);
                    stub.Method.Body = emitter.Emit(record, stub.Method);
                    result.Ok++;
                }
                catch (Exception ex)
                {
                    string reason = ex.GetType().Name + ": " + ex.Message;
                    result.Failures[reason] = result.Failures.TryGetValue(reason, out int c) ? c + 1 : 1;
                    result.Failed.Add(stub.Method.MDToken + " " + reason);
                }
            }
            return result;
        }

        public static void Run(string storePath, string assemblyPath, string outPath, string onlyTokensCsv = null)
        {
            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            byte[] store;
            if (storePath == "auto" || storePath == "-")
            {
                store = StoreCipher.DecryptStore(module, StoreCipher.DefaultResourceName);
                Console.WriteLine("[devirtall] decrypted store from assembly (" + store.Length + " bytes)");
            }
            else
            {
                store = File.ReadAllBytes(storePath);
            }

            HashSet<uint> only = null;
            if (!string.IsNullOrEmpty(onlyTokensCsv))
            {
                only = new HashSet<uint>(onlyTokensCsv.Split(',').Select(s => Convert.ToUInt32(s, 16)));
            }

            DevirtResult result = DevirtAllCli.Sweep(module, store, only);
            Console.WriteLine("[devirtall] interpreter " + result.Interpreter.FullName + " tok=" + result.Interpreter.MDToken);
            Console.WriteLine("[devirtall] devirtualized " + result.Ok + "/" + result.Total);
            if (result.Failures.Count > 0)
            {
                Console.WriteLine("[devirtall] failures by reason:");
                foreach (KeyValuePair<string, int> kv in result.Failures.OrderByDescending(k => k.Value))
                {
                    Console.WriteLine("  " + kv.Value + "x  " + kv.Key);
                }
            }
            if (result.Failed.Count > 0 && result.Failed.Count <= 40)
            {
                Console.WriteLine("[devirtall] failed methods:");
                foreach (string f in result.Failed)
                {
                    Console.WriteLine("  " + f);
                }
            }

            if (!string.IsNullOrEmpty(outPath))
            {
                ModuleWriterOptions options = new ModuleWriterOptions(module);
                options.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
                module.Write(outPath, options);
                Console.WriteLine("[devirtall] wrote " + outPath);
            }
        }
    }
}
