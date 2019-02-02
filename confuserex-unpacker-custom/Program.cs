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

            ModuleWriterOptions writerOptions = new ModuleWriterOptions(module);
            writerOptions.MetaDataOptions.Flags |= MetaDataFlags.PreserveAll;
            writerOptions.Logger = DummyLogger.NoThrowInstance;

            module.Write(path + "Cleaned.exe", writerOptions);
            Console.ReadLine();
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

            Protections.ControlFlowRun.cleaner(MainModule);
            try
            {
                Console.WriteLine("[!] Proxy Calls");
                int amountProxy = ProxyCalls.Execute();
                Console.WriteLine("[!] Amount Of Proxy Calls Fixed: " + amountProxy);

                Protections.ControlFlowRun.cleaner(MainModule);

                Console.WriteLine("[!] Decrytping Resources");
                Protections.ResourceDecrypt.Run(MainModule);

                Console.WriteLine("[!] Decrytping Strings");
                int strings = Protections.StaticStrings.Run(MainModule);
                Console.WriteLine("[!] Amount Of Strings Decrypted: " + strings);

                return ConfuserexUnpacker.Program.MainModule;
            }
            catch
            {
                return null;
            }
        }
    }
}
