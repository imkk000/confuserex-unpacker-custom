using dnlib.DotNet;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Protections.Devirt
{
    public sealed class ModulusCandidate
    {
        public long Offset;
        public string Tail;
        public bool Compressed;
        public bool Pkt;
        public bool Ascii;
    }

    public static class ConstPool
    {
        private const int Num = 1737506897;
        private static readonly int Num2 = -1931810529 + Num;
        private const int TailId = 30349956;

        private static int LcgMul => unchecked((1543023326 - Num) ^ Num2);
        private static int LcgAdd => unchecked((0x6C2B3502 ^ Num) + Num2);
        private static int C1 => unchecked((Num + -1884744145) ^ Num2);
        private static int C2 => unchecked((Num ^ 0x2633709A) + Num2);
        private static int Cflags => unchecked(-(~(-(~(~(-(-(~(~(-1507488129 - Num + Num2))))))))));
        private static int LenMask => unchecked((Num ^ 0x63FB693E) - Num2);
        private static int AsciiMask => unchecked(604280383 + Num + Num2);
        private static int LzMask => unchecked((469461441 - Num) ^ Num2);
        private static int Redirect => unchecked((Num + -1543203267) | Num2);
        private static short InitK1 => unchecked((short)(-(~(~(-(~(-(-(~(-(~(~((-1812286265 ^ Num) - Num2)))))))))))));
        private static short InitK2 => unchecked((short)(~(-(~(-(-(~(~(-(~(-1543171229 + Num + Num2)))))))))));

        public static byte[] FindConstResource(ModuleDefMD module)
        {
            foreach (Resource res in module.Resources)
            {
                if (!(res is EmbeddedResource er)) continue;
                string name = res.Name.String;
                bool whitespaceName = name.Length > 0;
                foreach (char c in name)
                {
                    if (c < 0x2000 || c > 0x200B) { whitespaceName = false; break; }
                }
                if (whitespaceName) return er.CreateReader().ToArray();
            }
            return null;
        }

        public static byte[] FindKey128(ModuleDefMD module)
        {
            foreach (TypeDef t in module.GetTypes())
            {
                foreach (FieldDef f in t.Fields)
                {
                    if (f.HasFieldRVA && f.InitialValue != null && f.InitialValue.Length == 128)
                    {
                        return f.InitialValue;
                    }
                }
            }
            return null;
        }

        public static short ReadNum6(byte[] pool)
        {
            return unchecked((short)((short)(pool[0] | (pool[1] << 8)) ^ InitK1));
        }

        public static short ReadShared0008(byte[] pool)
        {
            return unchecked((short)((short)(pool[2] | (pool[3] << 8)) ^ InitK2));
        }

        private static byte[] ReadGlobalKey(byte[] pool)
        {
            short num6 = ReadNum6(pool);
            if (num6 == 0)
            {
                return null;
            }
            byte[] key = new byte[num6];
            Buffer.BlockCopy(pool, 2, key, 0, num6);
            return key;
        }

        public static List<ModulusCandidate> Scan(byte[] pool, byte[] key128, byte[] storeResource)
        {
            List<ModulusCandidate> hits = new List<ModulusCandidate>();
            byte[] globalKey = ReadGlobalKey(pool);
            int sharedHeaderLen = globalKey == null ? ReadShared0008(pool) : -1;
            if (globalKey == null && (sharedHeaderLen < 3 || sharedHeaderLen == -1))
            {
                throw new InvalidOperationException("unsupported header path: shared0008=" + sharedHeaderLen);
            }

            int flagsUnmask = Cflags ^ TailId ^ C1 ^ C2;
            int posUnmask = TailId ^ C1 ^ C2;

            for (int p = 0; p + 4 <= pool.Length; p++)
            {
                byte[] header;
                int flagsOff;
                if (globalKey != null)
                {
                    header = globalKey;
                    flagsOff = p;
                }
                else
                {
                    if (p + sharedHeaderLen + 4 > pool.Length) continue;
                    int num7c = p ^ posUnmask;
                    header = new byte[sharedHeaderLen];
                    for (int i = 0; i < sharedHeaderLen; i++)
                    {
                        header[i] = unchecked((byte)(pool[p + i] ^ (byte)(num7c >> ((i & 3) << 3))));
                    }
                    flagsOff = p + sharedHeaderLen;
                }
                if (header.Length < 3) continue;
                if (flagsOff + 4 > pool.Length) continue;

                int raw = pool[flagsOff] | (pool[flagsOff + 1] << 8) | (pool[flagsOff + 2] << 16) | (pool[flagsOff + 3] << 24);
                int num23 = raw ^ flagsUnmask;
                if (num23 == Redirect) continue;
                int bodyLen = num23 & LenMask;
                if (bodyLen < 4 || bodyLen > 65536) continue;
                int bodyOff = flagsOff + 4;
                if (bodyOff + bodyLen > pool.Length) continue;

                byte[] body = new byte[bodyLen];
                Buffer.BlockCopy(pool, bodyOff, body, 0, bodyLen);
                Keystream(header, body);

                bool compressed = (num23 & LzMask) != 0;
                bool ascii = (num23 & AsciiMask) != 0;

                foreach (bool tryCompressed in compressed ? new[] { true, false } : new[] { false, true })
                {
                    byte[] data = tryCompressed ? Decompress(body) : body;
                    if (data == null) continue;
                    foreach (bool tryAscii in ascii ? new[] { true, false } : new[] { false, true })
                    {
                        string s = tryAscii ? AsciiString(data) : UnicodeString(data);
                        if (!LooksLikeModulusTail(s)) continue;
                        if (ValidatesAsModulus(s, key128, storeResource))
                        {
                            hits.Add(new ModulusCandidate
                            {
                                Offset = p,
                                Tail = s,
                                Compressed = tryCompressed,
                                Pkt = false,
                                Ascii = tryAscii
                            });
                        }
                    }
                }
            }
            return hits;
        }

        private static void Keystream(byte[] header, byte[] body)
        {
          unchecked
          {
            byte b = header[1];
            int len = body.Length;
            byte b2 = (byte)((11 + len) ^ (7 + b));
            uint state = (uint)((header[0] | (header[2] << 8)) + (b2 << 3));
            ushort key = 0;
            for (int i = 0; i < len; i++)
            {
                if ((1 & i) == 0)
                {
                    state = (uint)((int)state * LcgMul + LcgAdd);
                    key = (ushort)(state >> 16);
                }
                byte k = (byte)key;
                key >>= 8;
                byte cipher = body[i];
                body[i] = (byte)(cipher ^ b ^ (3 + b2) ^ k);
                b2 = cipher;
            }
          }
        }

        private static byte[] Decompress(byte[] src)
        {
            if (src.Length < 4) return null;
            int outLen = src[2] | (src[0] << 16) | (src[3] << 8) | (src[1] << 24);
            if (outLen <= 0 || outLen > (1 << 22)) return null;
            byte[] dst = new byte[outLen];
            int srcOff = 4;
            int pos = 0;
            int ctrl = 128;
            int ctrlByte = 0;
            try
            {
                while (pos < outLen)
                {
                    if ((ctrl <<= 1) == 256)
                    {
                        ctrl = 1;
                        ctrlByte = src[srcOff++];
                    }
                    if ((ctrlByte & ctrl) != 0)
                    {
                        int count = (src[srcOff] >> 2) + 3;
                        int dist = ((src[srcOff] << 8) | src[srcOff + 1]) & 0x3FF;
                        srcOff += 2;
                        int from = pos - dist;
                        if (from < 0) break;
                        while (--count >= 0 && pos < outLen)
                        {
                            dst[pos++] = dst[from++];
                        }
                    }
                    else
                    {
                        dst[pos++] = src[srcOff++];
                    }
                }
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
            return dst;
        }

        private static string AsciiString(byte[] data)
        {
            char[] chars = new char[data.Length];
            for (int i = 0; i < data.Length; i++) chars[i] = (char)data[i];
            return new string(chars);
        }

        private static string UnicodeString(byte[] data)
        {
            return System.Text.Encoding.Unicode.GetString(data);
        }

        private static bool LooksLikeModulusTail(string s)
        {
            if (s == null || s.Length < 64 || s.Length % 4 != 0) return false;
            foreach (char c in s)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                          c == '+' || c == '/' || c == '=';
                if (!ok) return false;
            }
            return true;
        }

        public static string RecoverModulusTail(ModuleDefMD module, byte[] key128, byte[] storeResource)
        {
            byte[] pool = FindConstResource(module);
            if (pool == null) throw new InvalidOperationException("constants resource not found");
            List<ModulusCandidate> hits = Scan(pool, key128, storeResource);
            if (hits.Count == 0) throw new InvalidOperationException("RSA modulus tail not recovered from constants pool");
            return hits[0].Tail;
        }

        private static int RedirectXor => unchecked(0x5F04E142 ^ Num ^ Num2);

        public static int RecoverNum7(byte[] pool, byte[] key128, byte[] storeResource)
        {
            List<ModulusCandidate> hits = Scan(pool, key128, storeResource);
            if (hits.Count == 0) throw new InvalidOperationException("num7 not recoverable: no modulus tail hit");
            return unchecked((int)hits[0].Offset ^ TailId ^ C1 ^ C2);
        }

        public static string DecodeString(byte[] pool, int id, int num7)
        {
            byte[] globalKey = ReadGlobalKey(pool);
            int sharedHeaderLen = globalKey == null ? ReadShared0008(pool) : -1;
            if (globalKey == null && sharedHeaderLen < 3)
            {
                throw new InvalidOperationException("unsupported header path: shared0008=" + sharedHeaderLen);
            }

            for (int guard = 0; guard < 64; guard++)
            {
                int offset = unchecked(id ^ C1 ^ C2 ^ num7);
                if (offset < 0 || offset + 4 > pool.Length) return null;

                byte[] header;
                int flagsOff;
                if (globalKey != null)
                {
                    header = globalKey;
                    flagsOff = offset;
                }
                else
                {
                    if (offset + sharedHeaderLen + 4 > pool.Length) return null;
                    header = new byte[sharedHeaderLen];
                    for (int i = 0; i < sharedHeaderLen; i++)
                    {
                        header[i] = unchecked((byte)(pool[offset + i] ^ (byte)(num7 >> ((i & 3) << 3))));
                    }
                    flagsOff = offset + sharedHeaderLen;
                }
                if (flagsOff + 4 > pool.Length) return null;

                int raw = pool[flagsOff] | (pool[flagsOff + 1] << 8) | (pool[flagsOff + 2] << 16) | (pool[flagsOff + 3] << 24);
                int num20 = unchecked(raw ^ offset ^ Cflags ^ num7);

                if (num20 == Redirect)
                {
                    if (flagsOff + 8 > pool.Length) return null;
                    int rb = flagsOff + 4;
                    id = unchecked((pool[rb + 2] | (pool[rb + 3] << 16) | (pool[rb] << 8) | (pool[rb + 1] << 24)) ^ -(RedirectXor ^ num7));
                    continue;
                }

                int bodyLen = num20 & LenMask;
                if (bodyLen < 1 || bodyLen > 0x100000) return null;
                int bodyOff = flagsOff + 4;
                if (bodyOff + bodyLen > pool.Length) return null;

                byte[] body = new byte[bodyLen];
                Buffer.BlockCopy(pool, bodyOff, body, 0, bodyLen);
                Keystream(header, body);

                bool compressed = (num20 & LzMask) != 0;
                bool ascii = (num20 & AsciiMask) != 0;
                byte[] data = compressed ? Decompress(body) : body;
                if (data == null) return null;
                return ascii ? AsciiString(data) : UnicodeString(data);
            }
            return null;
        }

        public static void DecryptStringsCli(string assemblyPath, int[] sampleIds)
        {
            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            byte[] pool = FindConstResource(module);
            byte[] key128 = FindKey128(module);
            byte[] store = StoreCipher.FindResource(module, StoreCipher.DefaultResourceName);
            if (pool == null || key128 == null || store == null)
            {
                Console.WriteLine("[decryptstrings] missing inputs pool=" + (pool != null) + " key128=" + (key128 != null) + " store=" + (store != null));
                return;
            }
            int num7 = RecoverNum7(pool, key128, store);
            Console.WriteLine("[decryptstrings] num7=" + num7);
            foreach (int id in sampleIds)
            {
                string s = DecodeString(pool, id, num7);
                Console.WriteLine("  id=" + id + " -> " + (s == null ? "<null>" : "\"" + s + "\""));
            }
        }

        public static void ScanCli(string assemblyPath)
        {
            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            byte[] pool = FindConstResource(module);
            byte[] key128 = FindKey128(module);
            byte[] store = StoreCipher.FindResource(module, StoreCipher.DefaultResourceName);
            Console.WriteLine("[constscan] poolLen=" + (pool?.Length ?? -1) + " key128=" + (key128 != null) + " storeLen=" + (store?.Length ?? -1));
            if (pool == null || key128 == null || store == null) return;

            byte[] gk = ReadGlobalKey(pool);
            Console.WriteLine("[constscan] num6=" + ReadNum6(pool) + " globalKeyLen=" + (gk?.Length ?? -1) + " shared0008=" + ReadShared0008(pool));

            List<ModulusCandidate> hits = Scan(pool, key128, store);
            Console.WriteLine("[constscan] hits=" + hits.Count);
            foreach (ModulusCandidate h in hits)
            {
                Console.WriteLine("  off=" + h.Offset + " lz=" + h.Compressed + " ascii=" + h.Ascii + " len=" + h.Tail.Length);
                Console.WriteLine("  tail=" + h.Tail);
            }
        }

        private static bool ValidatesAsModulus(string tailB64, byte[] key128, byte[] storeResource)
        {
            byte[] tail;
            try { tail = Convert.FromBase64String(tailB64); }
            catch (FormatException) { return false; }

            byte[] modBytes = new byte[key128.Length + tail.Length];
            Buffer.BlockCopy(key128, 0, modBytes, 0, key128.Length);
            Buffer.BlockCopy(tail, 0, modBytes, key128.Length, tail.Length);
            BigInteger n = new BigInteger(modBytes, isUnsigned: true, isBigEndian: true);
            if (n.Sign <= 0) return false;

            long bits = n.GetBitLength();
            int inputBlock = (int)((bits + 7) / 8);
            int outputBlock = (int)((bits - 1) / 8);
            if (inputBlock <= 0 || outputBlock <= 11) return false;
            if (4 + inputBlock > storeResource.Length) return false;

            byte[] block = new byte[inputBlock];
            Buffer.BlockCopy(storeResource, 4, block, 0, inputBlock);
            BigInteger c = new BigInteger(block, isUnsigned: true, isBigEndian: true);
            if (c >= n) return false;
            BigInteger m = BigInteger.ModPow(c, StoreCipher.Exponent, n);
            byte[] mb = m.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (mb.Length > outputBlock) return false;
            byte[] padded = new byte[outputBlock];
            Buffer.BlockCopy(mb, 0, padded, outputBlock - mb.Length, mb.Length);
            return padded[0] == 2;
        }
    }
}
