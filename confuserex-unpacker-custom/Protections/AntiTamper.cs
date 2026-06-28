using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.PE;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Protections
{
    static class AntiTamper
    {
        private static uint[] arrayKeys;
        private static byte[] byteResult;
        private static uint[] initialKeys;
        private static BinaryReader reader;
        private static MemoryStream input;

        public static void Run(ref ModuleDefMD module)
        {
            if (!IsTampered(module))
            {
                return;
            }
            Console.WriteLine("[!] Anti Tamper Detected");
            try
            {
                var peReader = module.Metadata.PEImage.CreateReader();
                byte[] rawbytes = peReader.ReadBytes((int)peReader.Length);
                ModuleDefMD cleaned = UnAntiTamper(module, rawbytes);
                if (cleaned != null)
                {
                    module = cleaned;
                }
            }
            catch
            {
                Console.WriteLine("[!] Anti Tamper Failed To Remove");
            }
        }

        public static bool IsTampered(ModuleDefMD module)
        {
            MethodDef cctor = module.GlobalType?.FindStaticConstructor();
            if (cctor == null || !cctor.HasBody || cctor.Body.Instructions.Count == 0)
            {
                return false;
            }
            if (cctor.Body.Instructions[0].OpCode != OpCodes.Call ||
                !(cctor.Body.Instructions[0].Operand is MethodDef))
            {
                return false;
            }
            foreach (ImageSectionHeader section in module.Metadata.PEImage.ImageSectionHeaders)
            {
                switch (section.DisplayName)
                {
                    case ".text":
                    case ".rsrc":
                    case ".reloc":
                        continue;
                    default:
                        return true;
                }
            }
            return false;
        }

        private static ModuleDefMD UnAntiTamper(ModuleDefMD module, byte[] rawbytes)
        {
            initialKeys = new uint[4];
            MethodDef cctor = module.GlobalType?.FindStaticConstructor();
            MethodDef antitamp = cctor?.Body.Instructions[0].Operand as MethodDef;
            if (antitamp == null)
            {
                return null;
            }
            IList<ImageSectionHeader> imageSectionHeaders = module.Metadata.PEImage.ImageSectionHeaders;
            ImageSectionHeader confSec = imageSectionHeaders[0];
            FindInitialKeys(antitamp);
            input = new MemoryStream(rawbytes);
            reader = new BinaryReader(input);
            Hash1(input, reader, imageSectionHeaders, confSec);
            arrayKeys = GetArrayKeys();
            DecryptMethods(reader, confSec, input);
            ModuleDefMD reloaded = ModuleDefMD.Load(input);
            reloaded.GlobalType.FindStaticConstructor().Body.Instructions.RemoveAt(0);
            return reloaded;
        }

        private static void DecryptMethods(BinaryReader reader, ImageSectionHeader confSec, Stream stream)
        {
            int num = (int)(confSec.SizeOfRawData >> 2);
            int pointerToRawData = (int)confSec.PointerToRawData;
            stream.Position = pointerToRawData;
            uint[] numArray = new uint[num];
            for (uint i = 0; i < num; i++)
            {
                uint num4 = reader.ReadUInt32();
                numArray[i] = num4 ^ arrayKeys[(int)(i & 15)];
                arrayKeys[(int)(i & 15)] = num4 + 0x3dbb2819;
            }
            byteResult = numArray.SelectMany(BitConverter.GetBytes).ToArray();
            stream.Position = pointerToRawData;
            stream.Write(byteResult, 0, byteResult.Length);
        }

        private static void FindInitialKeys(MethodDef antitamp)
        {
            int count = antitamp.Body.Instructions.Count;
            for (int i = 0; i + 1 < count; i++)
            {
                Instruction item = antitamp.Body.Instructions[i];
                if (!item.OpCode.Equals(OpCodes.Ldc_I4))
                {
                    continue;
                }
                Instruction next = antitamp.Body.Instructions[i + 1];
                if (!next.OpCode.Equals(OpCodes.Stloc_S))
                {
                    continue;
                }
                string slot = next.Operand.ToString();
                if (slot.Contains("V_10"))
                {
                    initialKeys[0] = (uint)(int)item.Operand;
                }
                if (slot.Contains("V_11"))
                {
                    initialKeys[1] = (uint)(int)item.Operand;
                }
                if (slot.Contains("V_12"))
                {
                    initialKeys[2] = (uint)(int)item.Operand;
                }
                if (slot.Contains("V_13"))
                {
                    initialKeys[3] = (uint)(int)item.Operand;
                }
            }
        }

        private static uint[] GetArrayKeys()
        {
            uint[] dst = new uint[0x10];
            uint[] src = new uint[0x10];
            for (int i = 0; i < 0x10; i++)
            {
                dst[i] = initialKeys[3];
                src[i] = initialKeys[1];
                initialKeys[0] = (initialKeys[1] >> 5) | (initialKeys[1] << 0x1b);
                initialKeys[1] = (initialKeys[2] >> 3) | (initialKeys[2] << 0x1d);
                initialKeys[2] = (initialKeys[3] >> 7) | (initialKeys[3] << 0x19);
                initialKeys[3] = (initialKeys[0] >> 11) | (initialKeys[0] << 0x15);
            }
            return DeriveKeyAntiTamp(dst, src);
        }

        public static uint[] DeriveKeyAntiTamp(uint[] dst, uint[] src)
        {
            uint[] numArray = new uint[0x10];
            for (int i = 0; i < 0x10; i++)
            {
                switch (i % 3)
                {
                    case 0:
                        numArray[i] = dst[i] ^ src[i];
                        break;
                    case 1:
                        numArray[i] = dst[i] * src[i];
                        break;
                    case 2:
                        numArray[i] = dst[i] + src[i];
                        break;
                }
            }
            return numArray;
        }

        private static void Hash1(Stream stream, BinaryReader reader, IList<ImageSectionHeader> sections, ImageSectionHeader confSec)
        {
            foreach (ImageSectionHeader header in sections)
            {
                if (header == confSec || header.DisplayName == "")
                {
                    continue;
                }
                int num = (int)(header.SizeOfRawData >> 2);
                int pointerToRawData = (int)header.PointerToRawData;
                stream.Position = pointerToRawData;
                for (int i = 0; i < num; i++)
                {
                    uint num4 = reader.ReadUInt32();
                    uint num5 = ((initialKeys[0] ^ num4) + initialKeys[1]) + (initialKeys[2] * initialKeys[3]);
                    initialKeys[0] = initialKeys[1];
                    initialKeys[1] = initialKeys[2];
                    initialKeys[1] = initialKeys[3];
                    initialKeys[3] = num5;
                }
            }
        }
    }
}
