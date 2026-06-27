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
        public static Assembly asm;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
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
