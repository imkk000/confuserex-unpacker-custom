using System;
using System.IO;
using System.Text;

namespace Protections.Devirt
{
    public static class StoreDump
    {
        public static void DumpAt(string storePath, long offset, int count)
        {
            byte[] store = File.ReadAllBytes(storePath);
            StoreReader reader = MethodStore.OpenStore(store);
            reader.Position = offset;
            byte[] data = reader.ReadBytes(count);
            Console.WriteLine("[dump] offset=" + offset + " count=" + count);
            for (int i = 0; i < data.Length; i += 16)
            {
                StringBuilder hex = new StringBuilder();
                StringBuilder asc = new StringBuilder();
                for (int j = 0; j < 16 && i + j < data.Length; j++)
                {
                    byte b = data[i + j];
                    hex.Append(b.ToString("X2")).Append(' ');
                    asc.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                Console.WriteLine((offset + i).ToString("D6") + "  " + hex.ToString().PadRight(48) + " " + asc);
            }
        }

        public static void Run(string storePath, string[] keys)
        {
            byte[] store = File.ReadAllBytes(storePath);
            Console.WriteLine("[store] " + storePath + " len=" + store.Length);
            foreach (string key in keys)
            {
                try
                {
                    byte[] decoded = Ascii85.Decode(key);
                    long offset = MethodStore.KeyToOffset(key);
                    bool inRange = offset >= 0 && offset < store.Length;
                    Console.WriteLine("[key] \"" + key + "\" ascii85bytes=" + decoded.Length +
                        " offset=" + offset + " (0x" + offset.ToString("X") + ") inRange=" + inRange);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[key] \"" + key + "\" ERROR " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }
}
