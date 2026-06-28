using dnlib.DotNet;
using dnlib.DotNet.Writer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Protections;
using dnlib.DotNet.Emit;

namespace ConfuserexUnpacker
{
    class Program
    {
        public static ModuleDefMD MainModule;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                return;
            }

            if (args[0] == "--dumpat")
            {
                Protections.Devirt.StoreDump.DumpAt(args[1], long.Parse(args[2]), int.Parse(args[3]));
                return;
            }

            if (args[0] == "--cipherinfo")
            {
                Protections.Devirt.StoreCipherCli.Info(args[1]);
                return;
            }

            if (args[0] == "--constscan")
            {
                Protections.Devirt.ConstPool.ScanCli(args[1]);
                return;
            }

            if (args[0] == "--decryptstrings")
            {
                int[] ids = new int[args.Length - 2];
                for (int i = 0; i < ids.Length; i++) ids[i] = int.Parse(args[2 + i]);
                Protections.Devirt.ConstPool.DecryptStringsCli(args[1], ids);
                return;
            }

            if (args[0] == "--listres")
            {
                Protections.Devirt.StoreCipherCli.ListResources(args[1]);
                return;
            }

            if (args[0] == "--decryptstore")
            {
                string dsOut = args.Length > 2 ? args[2] : null;
                string dsCompare = args.Length > 3 ? args[3] : null;
                Protections.Devirt.StoreCipherCli.Run(args[1], dsOut, dsCompare);
                return;
            }

            if (args[0] == "--disasm")
            {
                Protections.Devirt.Disassembler.Dump(args[1], long.Parse(args[2]));
                return;
            }

            if (args[0] == "--resolve")
            {
                Protections.Devirt.RefResolveCli.Dump(args[1], long.Parse(args[2]));
                return;
            }

            if (args[0] == "--resolvednlib")
            {
                Protections.Devirt.DnlibResolveCli.Dump(args[1], long.Parse(args[2]), args[3]);
                return;
            }

            if (args[0] == "--stripinit")
            {
                ModuleDefMD m = ModuleDefMD.Load(args[1]);
                TypeDef globalType = m.GlobalType;
                MethodDef cctor = globalType != null ? globalType.FindStaticConstructor() : null;
                if (cctor != null && cctor.HasBody)
                {
                    cctor.Body.Instructions.Clear();
                    cctor.Body.ExceptionHandlers.Clear();
                    cctor.Body.Variables.Clear();
                    cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                    Console.WriteLine("[stripinit] blanked <Module>::.cctor");
                }
                else
                {
                    Console.WriteLine("[stripinit] no global .cctor body found");
                }
                ModuleWriterOptions o = new ModuleWriterOptions(m);
                o.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
                m.Write(args[2], o);
                Console.WriteLine("[stripinit] wrote " + args[2]);
                return;
            }

            if (args[0] == "--refinfo")
            {
                Protections.Devirt.RefInfoCli.Run(args[1], long.Parse(args[2]), args[3]);
                return;
            }

            if (args[0] == "--findsig")
            {
                Protections.Devirt.FindSigCli.Run(args[1], args[2], args.Length > 3 ? args[3] : "");
                return;
            }

            if (args[0] == "--dumptype")
            {
                Protections.Devirt.DumpTypeCli.Run(args[1], long.Parse(args[2]), args[3]);
                return;
            }

            if (args[0] == "--probeeh")
            {
                byte[] store = File.ReadAllBytes(args[1]);
                long off = long.Parse(args[2]);
                var r = Protections.Devirt.MethodStore.OpenStore(store);
                var mrr = new Protections.Devirt.MethodRecordReader(r, false);
                mrr.ReadSignatureRecord(off);
                long ehStart = r.Position;
                Console.WriteLine("[probeeh] ehStart=" + ehStart);
                var map = Protections.Devirt.OpcodeMap.ByKey;
                foreach (int cw in new[] { 1, 2 })
                {
                    r.Position = ehStart;
                    int count = cw == 1 ? r.ReadByte8() : r.ReadUInt16();
                    Console.WriteLine("  countWidth=" + cw + " count=" + count);
                    foreach (int clauseW in new[] { 12, 16, 20, 24, 28, 32 })
                    {
                        long p = ehStart + cw + (long)count * clauseW;
                        if (p + 8 > store.Length) continue;
                        r.Position = p;
                        int len = r.ReadInt32();
                        long keyPos = p + 4;
                        r.Position = keyPos;
                        int key = r.ReadInt32();
                        bool keyOk = map.ContainsKey(key);
                        bool lenOk = len > 0 && len < 200000 && (p + 4 + len) <= store.Length;
                        if (keyOk && lenOk)
                        {
                            Console.WriteLine("    *** clauseW=" + clauseW + " bytecodeLenPos=" + p + " len=" + len + " firstKey=0x" + key.ToString("X8") + " VALID");
                        }
                    }
                }
                return;
            }

            if (args[0] == "--key2off")
            {
                Console.WriteLine(Protections.Devirt.MethodStore.KeyToOffset(args[1]));
                return;
            }

            if (args[0] == "--ehinfo")
            {
                byte[] st = File.ReadAllBytes(args[1]);
                ModuleDefMD em = ModuleDefMD.Load(args[2]);
                MethodDef ei = Protections.Devirt.StubDetector.FindInterpreter(em);
                foreach (var s in Protections.Devirt.StubDetector.FindStubs(em, ei))
                {
                    try
                    {
                        long o = Protections.Devirt.MethodStore.KeyToOffset(s.Key);
                        var rec = new Protections.Devirt.MethodRecordReader(Protections.Devirt.MethodStore.OpenStore(st), false).Read(o);
                        if (rec.Handlers.Count > 0)
                        {
                            Console.Write(s.Method.MDToken.Raw.ToString("X8") + " off=" + o + " codeLen=" + rec.Bytecode.Length + " :");
                            foreach (var c in rec.Handlers)
                                Console.Write(" {type=" + c.ClauseType + " hStart=" + c.HandlerStart + " try=[" + c.TryStart + ",+" + c.TryLength + "] extra=" + c.Extra + "}");
                            Console.WriteLine();
                        }
                    }
                    catch { }
                }
                return;
            }

            if (args[0] == "--liststubs")
            {
                ModuleDefMD lm = ModuleDefMD.Load(args[1]);
                MethodDef interp = Protections.Devirt.StubDetector.FindInterpreter(lm);
                var ls = Protections.Devirt.StubDetector.FindStubs(lm, interp);
                Console.WriteLine(string.Join(",", ls.Select(s => s.Method.MDToken.Raw.ToString("X8"))));
                return;
            }

            if (args[0] == "--devirtall")
            {
                string allOut = args.Length > 3 ? args[3] : null;
                string only = args.Length > 4 ? args[4] : null;
                Protections.Devirt.DevirtAllCli.Run(args[1], args[2], allOut, only);
                return;
            }

            if (args[0] == "--dumpil")
            {
                ModuleDefMD m = ModuleDefMD.Load(args[1]);
                MethodDef md = m.ResolveToken((int)Convert.ToInt64(args[2], 16)) as MethodDef;
                Console.WriteLine("[dumpil] " + md.FullName + " tok=" + md.MDToken);
                if (md.HasBody)
                {
                    foreach (var instr in md.Body.Instructions)
                    {
                        Console.WriteLine("  IL_" + instr.Offset.ToString("X4") + "  " + instr.OpCode.Name.PadRight(12) + " " + (instr.Operand?.ToString() ?? ""));
                    }
                }
                return;
            }

            if (args[0] == "--devirt")
            {
                string devirtOut = args.Length > 4 ? args[4] : null;
                Protections.Devirt.DevirtCli.Run(args[1], long.Parse(args[2]), args[3], devirtOut);
                return;
            }

            if (args[0] == "--dumprecord")
            {
                Protections.Devirt.MethodRecordReader.Dump(args[1], long.Parse(args[2]));
                return;
            }

            if (args[0] == "--dumpstore")
            {
                string[] keys = new string[args.Length - 2];
                Array.Copy(args, 2, keys, 0, keys.Length);
                Protections.Devirt.StoreDump.Run(args[1], keys);
                return;
            }

            string path = args[0];
            ModuleDefMD module = RunScript(path);
            if (module == null)
            {
                Console.WriteLine("[x] Could not load module: " + path);
                return;
            }

            ModuleWriterOptions writerOptions = new ModuleWriterOptions(module);
            writerOptions.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
            writerOptions.Logger = DummyLogger.NoThrowInstance;

            string outPath = path + "Cleaned.exe";
            module.Write(outPath, writerOptions);
            Console.WriteLine("[+] Wrote " + outPath);
        }

        static ModuleDefMD RunScript(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            MainModule = ModuleDefMD.Load(path);

            Console.WriteLine("[!] Anti Tamper");
            Protections.AntiTamper.Run(ref MainModule);

            Safe("Devirtualize", delegate
            {
                byte[] store = Protections.Devirt.StoreCipher.DecryptStore(MainModule, Protections.Devirt.StoreCipher.DefaultResourceName);
                Protections.Devirt.DevirtResult devirt = Protections.Devirt.DevirtAllCli.Sweep(MainModule, store);
                Console.WriteLine("[!] Devirtualized " + devirt.Ok + "/" + devirt.Total + " VM stubs");
            });

            Console.WriteLine("[!] Control Flow Run");
            Protections.ControlFlowRun.cleaner(MainModule);
            Safe("Proxy Calls", delegate
            {
                int amountProxy = ProxyCalls.Execute();
                Console.WriteLine("[!] Amount Of Proxy Calls Fixed: " + amountProxy);
            });
            Safe("Control Flow Run Again", delegate { Protections.ControlFlowRun.cleaner(MainModule); });
            Safe("Decrypting Resources", delegate { Protections.ResourceDecrypt.Run(MainModule); });
            Safe("Decrypting Strings", delegate
            {
                int strings = Protections.StaticStrings.Run(MainModule);
                Console.WriteLine("[!] Amount Of Strings Decrypted: " + strings);
            });
            Safe("Anti Debug", delegate { Protections.AntiDebug.Run(MainModule); });

            return ConfuserexUnpacker.Program.MainModule;
        }

        static void Safe(string name, Action action)
        {
            Console.WriteLine("[!] " + name);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[x] " + name + " failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
