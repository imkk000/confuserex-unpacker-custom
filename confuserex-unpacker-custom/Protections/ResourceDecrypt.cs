using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Protections
{
    class ResourceDecrypt
    {
        // Token: 0x06000003 RID: 3 RVA: 0x000020B8 File Offset: 0x000002B8
        public static void Run(ModuleDefMD moduleDefMD)
        {
            uint valuee = getfirst(moduleDefMD);
            uint[] uintValue = GetUintValue(moduleDefMD);
            uint second = getSecond(moduleDefMD);
            initialize(valuee, uintValue, second, moduleDefMD);
            RemoveCall(moduleDefMD);
        }

        // Token: 0x06000004 RID: 4 RVA: 0x00002310 File Offset: 0x00000510
        public static void RemoveCall(ModuleDefMD module)
        {
            foreach (TypeDef current in module.Types)
            {
                bool flag = current.Name.Equals("<Module>");
                if (flag)
                {
                    foreach (MethodDef current2 in current.Methods)
                    {
                        bool isStaticConstructor = current2.IsStaticConstructor;
                        if (isStaticConstructor)
                        {
                            for (int i = 0; i < current2.Body.Instructions.Count; i++)
                            {
                                bool flag2 = current2.Body.Instructions[i].OpCode == OpCodes.Call;
                                if (flag2)
                                {
                                    bool flag3 = current2.Body.Instructions[i].Operand.ToString().Contains(cctormethod.Name);
                                    if (flag3)
                                    {
                                        current2.Body.Instructions.RemoveAt(i);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Token: 0x06000005 RID: 5 RVA: 0x00002488 File Offset: 0x00000688
        internal static byte[] smethod_0(byte[] byte_0)
        {
            MemoryStream memoryStream = new MemoryStream(byte_0);
            Class1 @class = new Class1();
            byte[] array = new byte[5];
            memoryStream.Read(array, 0, 5);
            @class.method_5(array);
            long num = 0L;
            for (int i = 0; i < 8; i++)
            {
                int num2 = memoryStream.ReadByte();
                num |= (long)((long)((ulong)((byte)num2)) << 8 * i);
            }
            byte[] array2 = new byte[(int)num];
            MemoryStream stream_ = new MemoryStream(array2, true);
            long long_ = memoryStream.Length - 13L;
            @class.method_4(memoryStream, stream_, long_, num);
            return array2;
        }

        // Token: 0x06000006 RID: 6 RVA: 0x00002524 File Offset: 0x00000724
        public static uint getfirst(ModuleDefMD module)
        {
            foreach (TypeDef current in module.Types)
            {
                foreach (MethodDef current2 in current.Methods)
                {
                    bool hasBody = current2.HasBody;
                    if (hasBody)
                    {
                        bool flag = current2.Body.Instructions.Count > 100;
                        if (flag)
                        {
                            int local = current2.Body.Variables.Count;
                            bool flag2 = local == 13;
                            if (flag2)
                            {
                                bool flag3 = current2.Body.Instructions[5].OpCode == OpCodes.Ldtoken;
                                if (flag3)
                                {
                                    bool flag4 = current2.Body.Instructions[6].OpCode == OpCodes.Call;
                                    if (flag4)
                                    {
                                        bool flag5 = current2.Body.Instructions[0].OpCode == OpCodes.Ldc_I4;
                                        if (flag5)
                                        {
                                            for (int i = 0; i < current2.Body.Instructions.Count; i++)
                                            {
                                                bool flag6 = current2.Body.Instructions[i].OpCode == OpCodes.Callvirt && current2.Body.Instructions[i].Operand.ToString().Contains("AssemblyResolve") && current2.Body.Instructions[i - 1].OpCode == OpCodes.Newobj;
                                                if (flag6)
                                                {
                                                    uint result = (uint)current2.Body.Instructions[0].GetLdcI4Value();
                                                    cctormethod = current2;
                                                    return result;
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
            return 0u;
        }

        // Token: 0x06000007 RID: 7 RVA: 0x0000276C File Offset: 0x0000096C
        public static uint getSecond(ModuleDefMD module)
        {
            for (int i = 5; i < cctormethod.Body.Instructions.Count; i++)
            {
                bool flag = cctormethod.Body.Instructions[i].OpCode == OpCodes.Ldc_I4;
                if (flag)
                {
                    return (uint)cctormethod.Body.Instructions[i].GetLdcI4Value();
                }
            }
            return 0u;
        }

        // Token: 0x06000008 RID: 8 RVA: 0x000027F0 File Offset: 0x000009F0
        public static uint[] GetUintValue(ModuleDefMD module)
        {
            for (int i = 0; i < cctormethod.Body.Instructions.Count; i++)
            {
                bool flag2 = cctormethod.Body.Instructions[i].OpCode == OpCodes.Ldtoken;
                bool flag4 = flag2;
                if (flag4)
                {
                    bool flag3 = cctormethod.Body.Instructions[i + 1].OpCode == OpCodes.Call;
                    bool flag5 = flag3;
                    if (flag5)
                    {
                        FieldDef fieldDef = (FieldDef)cctormethod.Body.Instructions[i].Operand;
                        byte[] initialValue = fieldDef.InitialValue;
                        uint[] array = new uint[initialValue.Length / 4];
                        Buffer.BlockCopy(initialValue, 0, array, 0, initialValue.Length);
                        return array;
                    }
                }
            }
            return null;
        }

        // Token: 0x06000009 RID: 9 RVA: 0x000028DC File Offset: 0x00000ADC
        public static void initialize(uint valuee, uint[] arrayy, uint secondvalue, ModuleDefMD module)
        {
            uint[] array2 = new uint[16];
            uint num2 = secondvalue;
            for (int i = 0; i < 16; i++)
            {
                num2 ^= num2 >> 13;
                num2 ^= num2 << 25;
                num2 ^= num2 >> 27;
                array2[i] = num2;
            }
            int num3 = 0;
            int num4 = 0;
            uint[] array3 = new uint[16];
            byte[] array4 = new byte[valuee * 4u];
            while ((long)num3 < (long)((ulong)valuee))
            {
                for (int j = 0; j < 16; j++)
                {
                    array3[j] = arrayy[num3 + j];
                }
                array3[0] = (array3[0] ^ array2[0]);
                array3[1] = (array3[1] ^ array2[1]);
                array3[2] = (array3[2] ^ array2[2]);
                array3[3] = (array3[3] ^ array2[3]);
                array3[4] = (array3[4] ^ array2[4]);
                array3[5] = (array3[5] ^ array2[5]);
                array3[6] = (array3[6] ^ array2[6]);
                array3[7] = (array3[7] ^ array2[7]);
                array3[8] = (array3[8] ^ array2[8]);
                array3[9] = (array3[9] ^ array2[9]);
                array3[10] = (array3[10] ^ array2[10]);
                array3[11] = (array3[11] ^ array2[11]);
                array3[12] = (array3[12] ^ array2[12]);
                array3[13] = (array3[13] ^ array2[13]);
                array3[14] = (array3[14] ^ array2[14]);
                array3[15] = (array3[15] ^ array2[15]);
                for (int k = 0; k < 16; k++)
                {
                    uint num5 = array3[k];
                    array4[num4++] = (byte)num5;
                    array4[num4++] = (byte)(num5 >> 8);
                    array4[num4++] = (byte)(num5 >> 16);
                    array4[num4++] = (byte)(num5 >> 24);
                    array2[k] ^= num5;
                }
                num3 += 16;
            }
            assembly_0 = Assembly.Load(smethod_0(array4));
            AppDomain.CurrentDomain.AssemblyResolve += smethod_2;
            Module[] modules = assembly_0.GetModules();
            module.Resources.Clear();
            foreach (Module module2 in modules)
            {
                string[] manifestResourceNames = module2.Assembly.GetManifestResourceNames();
                foreach (string text in manifestResourceNames)
                {
                    Stream manifestResourceStream = module2.Assembly.GetManifestResourceStream(text);
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        manifestResourceStream.CopyTo(memoryStream);
                        byte[] data = memoryStream.ToArray();
                        module.Resources.Add(new EmbeddedResource(text, data, ManifestResourceAttributes.Public));
                    }
                }
            }
        }

        // Token: 0x0600000A RID: 10 RVA: 0x00002BCC File Offset: 0x00000DCC
        internal static Assembly smethod_2(object object_0, ResolveEventArgs resolveEventArgs_0)
        {
            bool flag = assembly_0.FullName == resolveEventArgs_0.Name;
            bool flag2 = flag;
            Assembly result;
            if (flag2)
            {
                result = assembly_0;
            }
            else
            {
                result = null;
            }
            return result;
        }

        // Token: 0x04000002 RID: 2
        public static string ExePath = "";

        // Token: 0x04000003 RID: 3
        public static MethodDef cctormethod = null;

        // Token: 0x04000004 RID: 4
        internal static Assembly assembly_0;

        // Token: 0x04000005 RID: 5
        public static MethodDef methodd = null;
        
        // Token: 0x02000006 RID: 6
        internal class Class1
        {
            // Token: 0x06000018 RID: 24 RVA: 0x0000306C File Offset: 0x0000126C
            internal Class1()
            {
                this.uint_0 = uint.MaxValue;
                int num = 0;
                while ((long)num < 4L)
                {
                    this.struct1_0[num] = new Struct1(6);
                    num++;
                }
            }

            // Token: 0x06000019 RID: 25 RVA: 0x00003160 File Offset: 0x00001360
            internal void method_0(uint uint_3)
            {
                bool flag = this.uint_0 != uint_3;
                bool flag2 = flag;
                if (flag2)
                {
                    this.uint_0 = uint_3;
                    this.uint_1 = Math.Max(this.uint_0, 1u);
                    uint uint_4 = Math.Max(this.uint_1, 4096u);
                    this.class4_0.method_0(uint_4);
                }
            }

            // Token: 0x0600001A RID: 26 RVA: 0x000031B9 File Offset: 0x000013B9
            internal void method_1(int int_0, int int_1)
            {
                this.class3_0.method_0(int_0, int_1);
            }

            // Token: 0x0600001B RID: 27 RVA: 0x000031CC File Offset: 0x000013CC
            internal void method_2(int int_0)
            {
                uint num = 1u << int_0;
                this.class2_0.method_0(num);
                this.class2_1.method_0(num);
                this.uint_2 = num - 1u;
            }

            // Token: 0x0600001C RID: 28 RVA: 0x00003204 File Offset: 0x00001404
            internal void method_3(Stream stream_0, Stream stream_1)
            {
                this.class0_0.method_0(stream_0);
                this.class4_0.method_1(stream_1, this.bool_0);
                for (uint num = 0u; num < 12u; num += 1u)
                {
                    for (uint num2 = 0u; num2 <= this.uint_2; num2 += 1u)
                    {
                        uint value = (num << 4) + num2;
                        this.struct0_0[(int)((uint)((UIntPtr)value))].method_0();
                        this.struct0_1[(int)((uint)((UIntPtr)value))].method_0();
                    }
                    this.struct0_2[(int)((uint)((UIntPtr)num))].method_0();
                    this.struct0_3[(int)((uint)((UIntPtr)num))].method_0();
                    this.struct0_4[(int)((uint)((UIntPtr)num))].method_0();
                    this.struct0_5[(int)((uint)((UIntPtr)num))].method_0();
                }
                this.class3_0.method_1();
                for (uint num3 = 0u; num3 < 4u; num3 += 1u)
                {
                    this.struct1_0[(int)((uint)((UIntPtr)num3))].method_0();
                }
                for (uint num4 = 0u; num4 < 114u; num4 += 1u)
                {
                    this.struct0_6[(int)((uint)((UIntPtr)num4))].method_0();
                }
                this.class2_0.method_1();
                this.class2_1.method_1();
                this.struct1_1.method_0();
            }

            // Token: 0x0600001D RID: 29 RVA: 0x000033B0 File Offset: 0x000015B0
            internal void method_4(Stream stream_0, Stream stream_1, long long_0, long long_1)
            {
                this.method_3(stream_0, stream_1);
                Struct3 @struct = default(Struct3);
                @struct.method_0();
                uint num = 0u;
                uint num2 = 0u;
                uint num3 = 0u;
                uint num4 = 0u;
                ulong num5 = 0UL;
                bool flag = 0L < long_1;
                bool flag12 = flag;
                if (flag12)
                {
                    this.struct0_0[(int)((uint)((UIntPtr)(@struct.uint_0 << 4)))].method_1(this.class0_0);
                    @struct.method_1();
                    byte byte_ = this.class3_0.method_3(this.class0_0, 0u, 0);
                    this.class4_0.method_5(byte_);
                    num5 += 1UL;
                }
                while (num5 < (ulong)long_1)
                {
                    uint num6 = (uint)num5 & this.uint_2;
                    bool flag2 = this.struct0_0[(int)((uint)((UIntPtr)((@struct.uint_0 << 4) + num6)))].method_1(this.class0_0) == 0u;
                    bool flag13 = flag2;
                    if (flag13)
                    {
                        byte byte_2 = this.class4_0.method_6(0u);
                        bool flag3 = !@struct.method_5();
                        bool flag14 = flag3;
                        byte byte_3;
                        if (flag14)
                        {
                            byte_3 = this.class3_0.method_4(this.class0_0, (uint)num5, byte_2, this.class4_0.method_6(num));
                        }
                        else
                        {
                            byte_3 = this.class3_0.method_3(this.class0_0, (uint)num5, byte_2);
                        }
                        this.class4_0.method_5(byte_3);
                        @struct.method_1();
                        num5 += 1UL;
                    }
                    else
                    {
                        bool flag4 = this.struct0_2[(int)((uint)((UIntPtr)@struct.uint_0))].method_1(this.class0_0) == 1u;
                        bool flag15 = flag4;
                        uint num8;
                        if (flag15)
                        {
                            bool flag5 = this.struct0_3[(int)((uint)((UIntPtr)@struct.uint_0))].method_1(this.class0_0) == 0u;
                            bool flag16 = flag5;
                            if (flag16)
                            {
                                bool flag6 = this.struct0_1[(int)((uint)((UIntPtr)((@struct.uint_0 << 4) + num6)))].method_1(this.class0_0) == 0u;
                                bool flag17 = flag6;
                                if (flag17)
                                {
                                    @struct.method_4();
                                    this.class4_0.method_5(this.class4_0.method_6(num));
                                    num5 += 1UL;
                                    continue;
                                }
                            }
                            else
                            {
                                bool flag7 = this.struct0_4[(int)((uint)((UIntPtr)@struct.uint_0))].method_1(this.class0_0) == 0u;
                                bool flag18 = flag7;
                                uint num7;
                                if (flag18)
                                {
                                    num7 = num2;
                                }
                                else
                                {
                                    bool flag8 = this.struct0_5[(int)((uint)((UIntPtr)@struct.uint_0))].method_1(this.class0_0) == 0u;
                                    bool flag19 = flag8;
                                    if (flag19)
                                    {
                                        num7 = num3;
                                    }
                                    else
                                    {
                                        num7 = num4;
                                        num4 = num3;
                                    }
                                    num3 = num2;
                                }
                                num2 = num;
                                num = num7;
                            }
                            num8 = this.class2_1.method_2(this.class0_0, num6) + 2u;
                            @struct.method_3();
                        }
                        else
                        {
                            num4 = num3;
                            num3 = num2;
                            num2 = num;
                            num8 = 2u + this.class2_0.method_2(this.class0_0, num6);
                            @struct.method_2();
                            uint num9 = this.struct1_0[(int)((uint)((UIntPtr)Class1.smethod_0(num8)))].method_1(this.class0_0);
                            bool flag9 = num9 >= 4u;
                            bool flag20 = flag9;
                            if (flag20)
                            {
                                int num10 = (int)((num9 >> 1) - 1u);
                                num = (2u | (num9 & 1u)) << num10;
                                bool flag10 = num9 < 14u;
                                bool flag21 = flag10;
                                if (flag21)
                                {
                                    num += Struct1.smethod_0(this.struct0_6, num - num9 - 1u, this.class0_0, num10);
                                }
                                else
                                {
                                    num += this.class0_0.method_3(num10 - 4) << 4;
                                    num += this.struct1_1.method_2(this.class0_0);
                                }
                            }
                            else
                            {
                                num = num9;
                            }
                        }
                        bool flag11 = ((ulong)num >= num5 || num >= this.uint_1) && num == uint.MaxValue;
                        bool flag22 = flag11;
                        if (flag22)
                        {
                            break;
                        }
                        this.class4_0.method_4(num, num8);
                        num5 += (ulong)num8;
                    }
                }
                this.class4_0.method_3();
                this.class4_0.method_2();
                this.class0_0.method_1();
            }

            // Token: 0x0600001E RID: 30 RVA: 0x000037F4 File Offset: 0x000019F4
            internal void method_5(byte[] byte_0)
            {
                int int_ = (int)(byte_0[0] % 9);
                int num = (int)(byte_0[0] / 9);
                int int_2 = num % 5;
                int int_3 = num / 5;
                uint num2 = 0u;
                for (int i = 0; i < 4; i++)
                {
                    num2 += (uint)((uint)byte_0[1 + i] << i * 8);
                }
                this.method_0(num2);
                this.method_1(int_2, int_);
                this.method_2(int_3);
            }

            // Token: 0x0600001F RID: 31 RVA: 0x00003860 File Offset: 0x00001A60
            internal static uint smethod_0(uint uint_3)
            {
                uint_3 -= 2u;
                bool flag = uint_3 < 4u;
                bool flag2 = flag;
                uint result;
                if (flag2)
                {
                    result = uint_3;
                }
                else
                {
                    result = 3u;
                }
                return result;
            }

            // Token: 0x0400000E RID: 14
            internal readonly Struct0[] struct0_0 = new Struct0[192];

            // Token: 0x0400000F RID: 15
            internal readonly Struct0[] struct0_1 = new Struct0[192];

            // Token: 0x04000010 RID: 16
            internal readonly Struct0[] struct0_2 = new Struct0[12];

            // Token: 0x04000011 RID: 17
            internal readonly Struct0[] struct0_3 = new Struct0[12];

            // Token: 0x04000012 RID: 18
            internal readonly Struct0[] struct0_4 = new Struct0[12];

            // Token: 0x04000013 RID: 19
            internal readonly Struct0[] struct0_5 = new Struct0[12];

            // Token: 0x04000014 RID: 20
            internal readonly Class1.Class2 class2_0 = new Class1.Class2();

            // Token: 0x04000015 RID: 21
            internal readonly Class1.Class3 class3_0 = new Class1.Class3();

            // Token: 0x04000016 RID: 22
            internal readonly Class4 class4_0 = new Class4();

            // Token: 0x04000017 RID: 23
            internal readonly Struct0[] struct0_6 = new Struct0[114];

            // Token: 0x04000018 RID: 24
            internal readonly Struct1[] struct1_0 = new Struct1[4];

            // Token: 0x04000019 RID: 25
            internal readonly Class0 class0_0 = new Class0();

            // Token: 0x0400001A RID: 26
            internal readonly Class1.Class2 class2_1 = new Class1.Class2();

            // Token: 0x0400001B RID: 27
            internal bool bool_0;

            // Token: 0x0400001C RID: 28
            internal uint uint_0;

            // Token: 0x0400001D RID: 29
            internal uint uint_1;

            // Token: 0x0400001E RID: 30
            internal Struct1 struct1_1 = new Struct1(4);

            // Token: 0x0400001F RID: 31
            internal uint uint_2;

            // Token: 0x0200000C RID: 12
            internal class Class2
            {
                // Token: 0x0600003A RID: 58 RVA: 0x00003FC4 File Offset: 0x000021C4
                internal void method_0(uint uint_1)
                {
                    for (uint num = this.uint_0; num < uint_1; num += 1u)
                    {
                        this.struct1_0[(int)((uint)((UIntPtr)num))] = new Struct1(3);
                        this.struct1_1[(int)((uint)((UIntPtr)num))] = new Struct1(3);
                    }
                    this.uint_0 = uint_1;
                }

                // Token: 0x0600003B RID: 59 RVA: 0x00004028 File Offset: 0x00002228
                internal void method_1()
                {
                    this.struct0_0.method_0();
                    for (uint num = 0u; num < this.uint_0; num += 1u)
                    {
                        this.struct1_0[(int)((uint)((UIntPtr)num))].method_0();
                        this.struct1_1[(int)((uint)((UIntPtr)num))].method_0();
                    }
                    this.struct0_1.method_0();
                    this.struct1_2.method_0();
                }

                // Token: 0x0600003C RID: 60 RVA: 0x000040AC File Offset: 0x000022AC
                internal uint method_2(Class0 class0_0, uint uint_1)
                {
                    bool flag = this.struct0_0.method_1(class0_0) == 0u;
                    bool flag3 = flag;
                    uint result;
                    if (flag3)
                    {
                        result = this.struct1_0[(int)((uint)((UIntPtr)uint_1))].method_1(class0_0);
                    }
                    else
                    {
                        uint num = 8u;
                        bool flag2 = this.struct0_1.method_1(class0_0) == 0u;
                        bool flag4 = flag2;
                        if (flag4)
                        {
                            num += this.struct1_1[(int)((uint)((UIntPtr)uint_1))].method_1(class0_0);
                        }
                        else
                        {
                            num += 8u;
                            num += this.struct1_2.method_1(class0_0);
                        }
                        result = num;
                    }
                    return result;
                }

                // Token: 0x0600003D RID: 61 RVA: 0x00004150 File Offset: 0x00002350
                internal Class2()
                {
                }

                // Token: 0x0400002C RID: 44
                internal readonly Struct1[] struct1_0 = new Struct1[16];

                // Token: 0x0400002D RID: 45
                internal readonly Struct1[] struct1_1 = new Struct1[16];

                // Token: 0x0400002E RID: 46
                internal Struct0 struct0_0 = default(Struct0);

                // Token: 0x0400002F RID: 47
                internal Struct0 struct0_1 = default(Struct0);

                // Token: 0x04000030 RID: 48
                internal Struct1 struct1_2 = new Struct1(8);

                // Token: 0x04000031 RID: 49
                internal uint uint_0;
            }

            // Token: 0x0200000D RID: 13
            internal class Class3
            {
                // Token: 0x0600003E RID: 62 RVA: 0x000041A4 File Offset: 0x000023A4
                internal void method_0(int int_2, int int_3)
                {
                    bool flag = this.struct2_0 != null && this.int_1 == int_3 && this.int_0 == int_2;
                    bool flag2 = !flag;
                    if (flag2)
                    {
                        this.int_0 = int_2;
                        this.uint_0 = (1u << int_2) - 1u;
                        this.int_1 = int_3;
                        uint num = 1u << this.int_1 + this.int_0;
                        this.struct2_0 = new Class1.Class3.Struct2[num];
                        for (uint num2 = 0u; num2 < num; num2 += 1u)
                        {
                            this.struct2_0[(int)((uint)((UIntPtr)num2))].method_0();
                        }
                    }
                }

                // Token: 0x0600003F RID: 63 RVA: 0x00004248 File Offset: 0x00002448
                internal void method_1()
                {
                    uint num = 1u << this.int_1 + this.int_0;
                    for (uint num2 = 0u; num2 < num; num2 += 1u)
                    {
                        this.struct2_0[(int)((uint)((UIntPtr)num2))].method_1();
                    }
                }

                // Token: 0x06000040 RID: 64 RVA: 0x00004298 File Offset: 0x00002498
                internal uint method_2(uint uint_1, byte byte_0)
                {
                    return ((uint_1 & this.uint_0) << this.int_1) + (uint)(byte_0 >> 8 - this.int_1);
                }

                // Token: 0x06000041 RID: 65 RVA: 0x000042CC File Offset: 0x000024CC
                internal byte method_3(Class0 class0_0, uint uint_1, byte byte_0)
                {
                    return this.struct2_0[(int)((uint)((UIntPtr)this.method_2(uint_1, byte_0)))].method_2(class0_0);
                }

                // Token: 0x06000042 RID: 66 RVA: 0x00004304 File Offset: 0x00002504
                internal byte method_4(Class0 class0_0, uint uint_1, byte byte_0, byte byte_1)
                {
                    return this.struct2_0[(int)((uint)((UIntPtr)this.method_2(uint_1, byte_0)))].method_3(class0_0, byte_1);
                }

                // Token: 0x06000043 RID: 67 RVA: 0x00002FC0 File Offset: 0x000011C0
                internal Class3()
                {
                }

                // Token: 0x04000032 RID: 50
                internal Class1.Class3.Struct2[] struct2_0;

                // Token: 0x04000033 RID: 51
                internal int int_0;

                // Token: 0x04000034 RID: 52
                internal int int_1;

                // Token: 0x04000035 RID: 53
                internal uint uint_0;

                // Token: 0x0200000E RID: 14
                internal struct Struct2
                {
                    // Token: 0x06000044 RID: 68 RVA: 0x0000433B File Offset: 0x0000253B
                    internal void method_0()
                    {
                        this.struct0_0 = new Struct0[768];
                    }

                    // Token: 0x06000045 RID: 69 RVA: 0x00004350 File Offset: 0x00002550
                    internal void method_1()
                    {
                        for (int i = 0; i < 768; i++)
                        {
                            this.struct0_0[i].method_0();
                        }
                    }

                    // Token: 0x06000046 RID: 70 RVA: 0x00004388 File Offset: 0x00002588
                    internal byte method_2(Class0 class0_0)
                    {
                        uint num = 1u;
                        do
                        {
                            num = (num << 1 | this.struct0_0[(int)((uint)((UIntPtr)num))].method_1(class0_0));
                        }
                        while (num < 256u);
                        return (byte)num;
                    }

                    // Token: 0x06000047 RID: 71 RVA: 0x000043D0 File Offset: 0x000025D0
                    internal byte method_3(Class0 class0_0, byte byte_0)
                    {
                        uint num = 1u;
                        for (; ; )
                        {
                            uint num2 = (uint)(byte_0 >> 7 & 1);
                            byte_0 = (byte)(byte_0 << 1);
                            uint num3 = this.struct0_0[(int)((uint)((UIntPtr)((1u + num2 << 8) + num)))].method_1(class0_0);
                            num = (num << 1 | num3);
                            bool flag = num2 != num3;
                            bool flag3 = flag;
                            if (flag3)
                            {
                                break;
                            }
                            bool flag2 = num >= 256u;
                            bool flag4 = flag2;
                            if (flag4)
                            {
                                goto Block_2;
                            }
                        }
                        while (num < 256u)
                        {
                            num = (num << 1 | this.struct0_0[(int)((uint)((UIntPtr)num))].method_1(class0_0));
                        }
                    Block_2:
                        return (byte)num;
                    }

                    // Token: 0x04000036 RID: 54
                    internal Struct0[] struct0_0;
                }
            }
        }

        // Token: 0x02000007 RID: 7
        internal class Class4
        {
            // Token: 0x06000020 RID: 32 RVA: 0x0000388C File Offset: 0x00001A8C
            internal void method_0(uint uint_3)
            {
                bool flag = this.uint_2 != uint_3;
                bool flag2 = flag;
                if (flag2)
                {
                    this.byte_0 = new byte[uint_3];
                }
                this.uint_2 = uint_3;
                this.uint_0 = 0u;
                this.uint_1 = 0u;
            }

            // Token: 0x06000021 RID: 33 RVA: 0x000038D0 File Offset: 0x00001AD0
            internal void method_1(Stream stream_1, bool bool_0)
            {
                this.method_2();
                this.stream_0 = stream_1;
                bool flag = !bool_0;
                bool flag2 = flag;
                if (flag2)
                {
                    this.uint_1 = 0u;
                    this.uint_0 = 0u;
                }
            }

            // Token: 0x06000022 RID: 34 RVA: 0x00003906 File Offset: 0x00001B06
            internal void method_2()
            {
                this.method_3();
                this.stream_0 = null;
                Buffer.BlockCopy(new byte[this.byte_0.Length], 0, this.byte_0, 0, this.byte_0.Length);
            }

            // Token: 0x06000023 RID: 35 RVA: 0x0000393C File Offset: 0x00001B3C
            internal void method_3()
            {
                uint num = this.uint_0 - this.uint_1;
                bool flag = num == 0u;
                bool flag3 = !flag;
                if (flag3)
                {
                    this.stream_0.Write(this.byte_0, (int)this.uint_1, (int)num);
                    bool flag2 = this.uint_0 >= this.uint_2;
                    bool flag4 = flag2;
                    if (flag4)
                    {
                        this.uint_0 = 0u;
                    }
                    this.uint_1 = this.uint_0;
                }
            }

            // Token: 0x06000024 RID: 36 RVA: 0x000039B0 File Offset: 0x00001BB0
            internal void method_4(uint uint_3, uint uint_4)
            {
                uint num = this.uint_0 - uint_3 - 1u;
                bool flag = num >= this.uint_2;
                bool flag4 = flag;
                if (flag4)
                {
                    num += this.uint_2;
                }
                while (uint_4 > 0u)
                {
                    bool flag2 = num >= this.uint_2;
                    bool flag5 = flag2;
                    if (flag5)
                    {
                        num = 0u;
                    }
                    byte[] arg_75_0 = this.byte_0;
                    uint num2 = this.uint_0;
                    this.uint_0 = num2 + 1u;
                    arg_75_0[(int)((uint)((UIntPtr)num2))] = this.byte_0[(int)((uint)((UIntPtr)(num++)))];
                    bool flag3 = this.uint_0 >= this.uint_2;
                    bool flag6 = flag3;
                    if (flag6)
                    {
                        this.method_3();
                    }
                    uint_4 -= 1u;
                }
            }

            // Token: 0x06000025 RID: 37 RVA: 0x00003A7C File Offset: 0x00001C7C
            internal void method_5(byte byte_1)
            {
                byte[] arg_23_0 = this.byte_0;
                uint num = this.uint_0;
                this.uint_0 = num + 1u;
                arg_23_0[(int)((uint)((UIntPtr)num))] = byte_1;
                bool flag = this.uint_0 >= this.uint_2;
                bool flag2 = flag;
                if (flag2)
                {
                    this.method_3();
                }
            }

            // Token: 0x06000026 RID: 38 RVA: 0x00003AD0 File Offset: 0x00001CD0
            internal byte method_6(uint uint_3)
            {
                uint num = this.uint_0 - uint_3 - 1u;
                bool flag = num >= this.uint_2;
                bool flag2 = flag;
                if (flag2)
                {
                    num += this.uint_2;
                }
                return this.byte_0[(int)((uint)((UIntPtr)num))];
            }

            // Token: 0x06000027 RID: 39 RVA: 0x00002FC0 File Offset: 0x000011C0
            internal Class4()
            {
            }

            // Token: 0x04000020 RID: 32
            internal byte[] byte_0;

            // Token: 0x04000021 RID: 33
            internal uint uint_0;

            // Token: 0x04000022 RID: 34
            internal Stream stream_0;

            // Token: 0x04000023 RID: 35
            internal uint uint_1;

            // Token: 0x04000024 RID: 36
            internal uint uint_2;
        }

        // Token: 0x02000008 RID: 8
        internal struct Struct0
        {
            // Token: 0x06000028 RID: 40 RVA: 0x00003B1C File Offset: 0x00001D1C
            internal void method_0()
            {
                this.uint_0 = 1024u;
            }

            // Token: 0x06000029 RID: 41 RVA: 0x00003B2C File Offset: 0x00001D2C
            internal uint method_1(Class0 class0_0)
            {
                uint num = (class0_0.uint_1 >> 11) * this.uint_0;
                bool flag = class0_0.uint_0 < num;
                bool flag4 = flag;
                uint result;
                if (flag4)
                {
                    class0_0.uint_1 = num;
                    this.uint_0 += 2048u - this.uint_0 >> 5;
                    bool flag2 = class0_0.uint_1 < 16777216u;
                    bool flag5 = flag2;
                    if (flag5)
                    {
                        class0_0.uint_0 = (class0_0.uint_0 << 8 | (uint)((byte)class0_0.stream_0.ReadByte()));
                        class0_0.uint_1 <<= 8;
                    }
                    result = 0u;
                }
                else
                {
                    class0_0.uint_1 -= num;
                    class0_0.uint_0 -= num;
                    this.uint_0 -= this.uint_0 >> 5;
                    bool flag3 = class0_0.uint_1 < 16777216u;
                    bool flag6 = flag3;
                    if (flag6)
                    {
                        class0_0.uint_0 = (class0_0.uint_0 << 8 | (uint)((byte)class0_0.stream_0.ReadByte()));
                        class0_0.uint_1 <<= 8;
                    }
                    result = 1u;
                }
                return result;
            }

            // Token: 0x04000025 RID: 37
            internal uint uint_0;
        }

        // Token: 0x02000009 RID: 9
        internal struct Struct1
        {
            // Token: 0x0600002A RID: 42 RVA: 0x00003C42 File Offset: 0x00001E42
            internal Struct1(int int_1)
            {
                this.int_0 = int_1;
                this.struct0_0 = new Struct0[1 << int_1];
            }

            // Token: 0x0600002B RID: 43 RVA: 0x00003C60 File Offset: 0x00001E60
            internal void method_0()
            {
                uint num = 1u;
                while ((ulong)num < 1UL << (this.int_0 & 31))
                {
                    this.struct0_0[(int)((uint)((UIntPtr)num))].method_0();
                    num += 1u;
                }
            }

            // Token: 0x0600002C RID: 44 RVA: 0x00003CAC File Offset: 0x00001EAC
            internal uint method_1(Class0 class0_0)
            {
                uint num = 1u;
                for (int i = this.int_0; i > 0; i--)
                {
                    num = (num << 1) + this.struct0_0[(int)((uint)((UIntPtr)num))].method_1(class0_0);
                }
                return num - (1u << this.int_0);
            }

            // Token: 0x0600002D RID: 45 RVA: 0x00003D08 File Offset: 0x00001F08
            internal uint method_2(Class0 class0_0)
            {
                uint num = 1u;
                uint num2 = 0u;
                for (int i = 0; i < this.int_0; i++)
                {
                    uint num3 = this.struct0_0[(int)((uint)((UIntPtr)num))].method_1(class0_0);
                    num <<= 1;
                    num += num3;
                    num2 |= num3 << i;
                }
                return num2;
            }

            // Token: 0x0600002E RID: 46 RVA: 0x00003D68 File Offset: 0x00001F68
            internal static uint smethod_0(Struct0[] struct0_1, uint uint_0, Class0 class0_0, int int_1)
            {
                uint num = 1u;
                uint num2 = 0u;
                for (int i = 0; i < int_1; i++)
                {
                    uint num3 = struct0_1[(int)((uint)((UIntPtr)(uint_0 + num)))].method_1(class0_0);
                    num <<= 1;
                    num += num3;
                    num2 |= num3 << i;
                }
                return num2;
            }

            // Token: 0x04000026 RID: 38
            internal readonly Struct0[] struct0_0;

            // Token: 0x04000027 RID: 39
            internal readonly int int_0;
        }

        // Token: 0x0200000A RID: 10
        internal struct Struct3
        {
            // Token: 0x0600002F RID: 47 RVA: 0x00003DC0 File Offset: 0x00001FC0
            internal void method_0()
            {
                this.uint_0 = 0u;
            }

            // Token: 0x06000030 RID: 48 RVA: 0x00003DCC File Offset: 0x00001FCC
            internal void method_1()
            {
                bool flag = this.uint_0 < 4u;
                bool flag3 = flag;
                if (flag3)
                {
                    this.uint_0 = 0u;
                }
                else
                {
                    bool flag2 = this.uint_0 < 10u;
                    bool flag4 = flag2;
                    if (flag4)
                    {
                        this.uint_0 -= 3u;
                    }
                    else
                    {
                        this.uint_0 -= 6u;
                    }
                }
            }

            // Token: 0x06000031 RID: 49 RVA: 0x00003E28 File Offset: 0x00002028
            internal void method_2()
            {
                this.uint_0 = ((this.uint_0 < 7u) ? 7u : 10u);
            }

            // Token: 0x06000032 RID: 50 RVA: 0x00003E3F File Offset: 0x0000203F
            internal void method_3()
            {
                this.uint_0 = ((this.uint_0 < 7u) ? 8u : 11u);
            }

            // Token: 0x06000033 RID: 51 RVA: 0x00003E56 File Offset: 0x00002056
            internal void method_4()
            {
                this.uint_0 = ((this.uint_0 < 7u) ? 9u : 11u);
            }

            // Token: 0x06000034 RID: 52 RVA: 0x00003E70 File Offset: 0x00002070
            internal bool method_5()
            {
                return this.uint_0 < 7u;
            }

            // Token: 0x04000028 RID: 40
            internal uint uint_0;
        }

        // Token: 0x0200000B RID: 11
        internal class Class0
        {
            // Token: 0x06000035 RID: 53 RVA: 0x00003E8C File Offset: 0x0000208C
            internal void method_0(Stream stream_1)
            {
                this.stream_0 = stream_1;
                this.uint_0 = 0u;
                this.uint_1 = uint.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    this.uint_0 = (this.uint_0 << 8 | (uint)((byte)this.stream_0.ReadByte()));
                }
            }

            // Token: 0x06000036 RID: 54 RVA: 0x00003EDC File Offset: 0x000020DC
            internal void method_1()
            {
                this.stream_0 = null;
            }

            // Token: 0x06000037 RID: 55 RVA: 0x00003EE8 File Offset: 0x000020E8
            internal void method_2()
            {
                while (this.uint_1 < 16777216u)
                {
                    this.uint_0 = (this.uint_0 << 8 | (uint)((byte)this.stream_0.ReadByte()));
                    this.uint_1 <<= 8;
                }
            }

            // Token: 0x06000038 RID: 56 RVA: 0x00003F34 File Offset: 0x00002134
            internal uint method_3(int int_0)
            {
                uint num = this.uint_1;
                uint num2 = this.uint_0;
                uint num3 = 0u;
                for (int i = int_0; i > 0; i--)
                {
                    num >>= 1;
                    uint num4 = num2 - num >> 31;
                    num2 -= (num & num4 - 1u);
                    num3 = (num3 << 1 | 1u - num4);
                    bool flag = num < 16777216u;
                    bool flag2 = flag;
                    if (flag2)
                    {
                        num2 = (num2 << 8 | (uint)((byte)this.stream_0.ReadByte()));
                        num <<= 8;
                    }
                }
                this.uint_1 = num;
                this.uint_0 = num2;
                return num3;
            }

            // Token: 0x06000039 RID: 57 RVA: 0x00002FC0 File Offset: 0x000011C0
            internal Class0()
            {
            }

            // Token: 0x04000029 RID: 41
            internal uint uint_0;

            // Token: 0x0400002A RID: 42
            internal uint uint_1;

            // Token: 0x0400002B RID: 43
            internal Stream stream_0;
        }
    }
}
