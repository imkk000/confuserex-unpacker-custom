using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Protections.Devirt
{
    public sealed class CipherParams
    {
        public string ResourceName;
        public byte[] Key128;
        public string TailBase64;
        public byte[] Resource;
    }

    public static class StoreCipher
    {
        public const int Exponent = 65537;
        public const string DefaultResourceName = "46b5e3665ee84dc4c8a9fcfc1596594e";

        public static byte[] FindResource(ModuleDefMD module, string name)
        {
            foreach (Resource res in module.Resources)
            {
                if (res.Name == name && res is EmbeddedResource er)
                {
                    return er.CreateReader().ToArray();
                }
            }
            return null;
        }

        public static CipherParams RecoverParams(ModuleDefMD module, string resourceName)
        {
            byte[] key128 = ConstPool.FindKey128(module);
            if (key128 == null) throw new InvalidOperationException("128-byte modulus key not found");
            byte[] resource = FindResource(module, resourceName);
            if (resource == null) throw new InvalidOperationException("manifest resource not found: " + resourceName);
            string tail = ConstPool.RecoverModulusTail(module, key128, resource);
            return new CipherParams
            {
                ResourceName = resourceName,
                Key128 = key128,
                TailBase64 = tail,
                Resource = resource
            };
        }

        public static byte[] DecryptStore(ModuleDefMD module, string resourceName)
        {
            return Decrypt(RecoverParams(module, resourceName));
        }

        public static byte[] Decrypt(CipherParams p)
        {
            byte[] tail = Convert.FromBase64String(p.TailBase64);
            byte[] modulusBytes = new byte[p.Key128.Length + tail.Length];
            Buffer.BlockCopy(p.Key128, 0, modulusBytes, 0, p.Key128.Length);
            Buffer.BlockCopy(tail, 0, modulusBytes, p.Key128.Length, tail.Length);
            BigInteger n = new BigInteger(modulusBytes, isUnsigned: true, isBigEndian: true);

            long bits = n.GetBitLength();
            int inputBlock = (int)((bits + 7) / 8);
            int outputBlock = (int)((bits - 1) / 8);

            byte[] resource = p.Resource;
            int total = ReadHeaderLength(resource);

            using MemoryStream output = new MemoryStream(total);
            int offset = 4;
            while (offset + inputBlock <= resource.Length && output.Length < total)
            {
                byte[] chunk = DecryptBlock(resource, offset, inputBlock, outputBlock, n);
                output.Write(chunk, 0, chunk.Length);
                offset += inputBlock;
            }

            byte[] result = output.ToArray();
            if (result.Length > total)
            {
                Array.Resize(ref result, total);
            }
            return result;
        }

        private static byte[] DecryptBlock(byte[] src, int offset, int inputBlock, int outputBlock, BigInteger n)
        {
            byte[] block = new byte[inputBlock];
            Buffer.BlockCopy(src, offset, block, 0, inputBlock);
            BigInteger c = new BigInteger(block, isUnsigned: true, isBigEndian: true);
            BigInteger m = BigInteger.ModPow(c, Exponent, n);
            byte[] mb = m.ToByteArray(isUnsigned: true, isBigEndian: true);

            byte[] padded = new byte[outputBlock];
            if (mb.Length >= outputBlock)
            {
                Buffer.BlockCopy(mb, mb.Length - outputBlock, padded, 0, outputBlock);
            }
            else
            {
                Buffer.BlockCopy(mb, 0, padded, outputBlock - mb.Length, mb.Length);
            }

            if (padded[0] != 2)
            {
                throw new InvalidOperationException("bad PKCS1 block header: 0x" + padded[0].ToString("X2"));
            }
            int sep = 1;
            while (sep < outputBlock && padded[sep] != 0)
            {
                sep++;
            }
            int dataStart = sep + 1;
            if (dataStart < 10 || dataStart > outputBlock)
            {
                throw new InvalidOperationException("bad PKCS1 padding length: " + dataStart);
            }
            int len = outputBlock - dataStart;
            byte[] data = new byte[len];
            Buffer.BlockCopy(padded, dataStart, data, 0, len);
            return data;
        }

        private static int ReadHeaderLength(byte[] resource)
        {
            StoreReader reader = new StoreReader(new PositionalXorStream(new MemoryStream(resource), 0));
            return reader.ReadInt32();
        }
    }

    public static class StoreCipherCli
    {
        public static void ListResources(string assemblyPath)
        {
            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            foreach (Resource res in module.Resources)
            {
                long len = res is EmbeddedResource er ? er.Length : -1;
                string printable = string.Concat(res.Name.String.Select(c => c < 32 || c > 126 ? '.' : c));
                Console.WriteLine("[res] len=" + len + " name='" + printable + "' (" +
                    string.Join(" ", res.Name.String.Select(c => "U+" + ((int)c).ToString("X4"))) + ")");
            }
        }

        public static void Info(string assemblyPath)
        {
            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            CipherParams p = StoreCipher.RecoverParams(module, StoreCipher.DefaultResourceName);
            Console.WriteLine("[cipherinfo] resource=" + p.ResourceName + " resourceLen=" + (p.Resource?.Length ?? -1));
            Console.WriteLine("[cipherinfo] key128=" + BitConverter.ToString(p.Key128).Replace("-", string.Empty));
            Console.WriteLine("[cipherinfo] tailB64=" + p.TailBase64);
            byte[] tail = Convert.FromBase64String(p.TailBase64);
            Console.WriteLine("[cipherinfo] tailLen=" + tail.Length + " modulusLen=" + (p.Key128.Length + tail.Length));
        }

        public static void Run(string assemblyPath, string outPath, string comparePath)
        {
            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            byte[] store = StoreCipher.DecryptStore(module, StoreCipher.DefaultResourceName);
            Console.WriteLine("[decryptstore] produced " + store.Length + " bytes");

            if (!string.IsNullOrEmpty(comparePath) && File.Exists(comparePath))
            {
                byte[] expected = File.ReadAllBytes(comparePath);
                bool equal = store.Length == expected.Length && store.SequenceEqual(expected);
                if (equal)
                {
                    Console.WriteLine("[decryptstore] MATCH vs " + comparePath + " (" + expected.Length + " bytes)");
                }
                else
                {
                    int firstDiff = -1;
                    int n = Math.Min(store.Length, expected.Length);
                    for (int i = 0; i < n; i++)
                    {
                        if (store[i] != expected[i]) { firstDiff = i; break; }
                    }
                    Console.WriteLine("[decryptstore] MISMATCH vs " + comparePath +
                        " (got " + store.Length + ", expected " + expected.Length + ", firstDiff=" + firstDiff + ")");
                }
            }

            if (!string.IsNullOrEmpty(outPath))
            {
                File.WriteAllBytes(outPath, store);
                Console.WriteLine("[decryptstore] wrote " + outPath);
            }
        }
    }
}
