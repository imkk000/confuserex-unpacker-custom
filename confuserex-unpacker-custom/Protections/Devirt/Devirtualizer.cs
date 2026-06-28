using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace Protections.Devirt
{
    public static class Devirtualizer
    {
        public static MethodDef FindStub(ModuleDefMD module, DnlibResolver resolver, MethodSignature sig)
        {
            bool isInstance = (sig.Flags & 1) != 0;
            List<ParamEntry> realParams = sig.Parameters.Skip(isInstance ? 1 : 0).ToList();
            string returnFullName = resolver.ResolveTypeSig(sig.ReturnTypeId).FullName;
            List<string> paramFullNames = realParams
                .Select(p => p.ByRef
                    ? new ByRefSig(resolver.ResolveTypeSig(p.TypeId)).FullName
                    : resolver.ResolveTypeSig(p.TypeId).FullName)
                .ToList();

            List<MethodDef> candidates = new List<MethodDef>();
            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef m in type.Methods)
                {
                    if (Matches(m, sig.Name, isInstance, returnFullName, paramFullNames))
                    {
                        candidates.Add(m);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("no stub matches name=" + Escape(sig.Name) +
                    " sig=" + returnFullName + "(" + string.Join(", ", paramFullNames) + ") instance=" + isInstance);
            }
            if (candidates.Count > 1)
            {
                throw new InvalidOperationException("ambiguous stub name=" + Escape(sig.Name) +
                    " sig=" + returnFullName + "(" + string.Join(", ", paramFullNames) + "): " + candidates.Count + " matches");
            }
            return candidates[0];
        }

        private static bool Matches(MethodDef m, string name, bool isInstance, string returnFullName, List<string> paramFullNames)
        {
            if (m.Name != name || m.IsStatic == isInstance || m.MethodSig == null)
            {
                return false;
            }
            if (m.MethodSig.Params.Count != paramFullNames.Count)
            {
                return false;
            }
            if (m.ReturnType.FullName != returnFullName)
            {
                return false;
            }
            for (int i = 0; i < paramFullNames.Count; i++)
            {
                if (m.MethodSig.Params[i].FullName != paramFullNames[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static string Escape(string s)
        {
            if (s == null)
            {
                return "<null>";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (c >= 0x20 && c < 0x7F)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("\\u").Append(((int)c).ToString("X4"));
                }
            }
            return sb.ToString();
        }
    }

    public static class DumpTypeCli
    {
        public static void Run(string storePath, long typeId, string assemblyPath)
        {
            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            TypeDef type;
            ITypeDefOrRef tdr;
            if (storePath == "-")
            {
                type = module.ResolveToken((int)typeId) as TypeDef;
                tdr = type;
            }
            else
            {
                byte[] store = File.ReadAllBytes(storePath);
                DnlibResolver resolver = new DnlibResolver(module, store);
                tdr = resolver.ResolveTypeSig(typeId).ToTypeDefOrRef();
                type = tdr as TypeDef ?? tdr.ResolveTypeDef();
            }
            Console.WriteLine("[type] id=" + typeId + " -> tok=" + tdr.MDToken + " isTypeDef=" + (type != null) + " full=" + tdr.FullName);
            if (type == null)
            {
                return;
            }
            foreach (MethodDef m in type.Methods)
            {
                string ps = string.Join(", ", m.MethodSig != null ? m.MethodSig.Params.Select(p => p.FullName) : Enumerable.Empty<string>());
                Console.WriteLine("  name=" + EscapeName(m.Name) + " static=" + m.IsStatic +
                    " ret=" + m.ReturnType.FullName + " params=(" + ps + ")");
            }
        }

        private static string EscapeName(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "")
            {
                sb.Append(c >= 0x20 && c < 0x7F ? c.ToString() : "\\u" + ((int)c).ToString("X4"));
            }
            return sb.ToString();
        }
    }

    public static class RefInfoCli
    {
        public static void Run(string storePath, long id, string assemblyPath)
        {
            byte[] store = File.ReadAllBytes(storePath);
            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);

            long declId = -1;
            string declName = null;
            try
            {
                ReferenceRecord rec = new RefRecordReader(MethodStore.OpenStore(store)).Read(id);
                Console.WriteLine("[refinfo] id=" + id + " category=" + rec.Category);
                if (rec.Category == RefCategory.Method)
                {
                    declId = rec.Method.DeclaringTypeId;
                    Console.WriteLine("  method name=" + Esc(rec.Method.Name) + " declTypeId=" + declId + " retId=" + rec.Method.ReturnTypeId);
                    DnlibResolver dr = new DnlibResolver(module, store);
                    foreach (long pid in rec.Method.ParameterTypeIds)
                    {
                        string r;
                        try { TypeSig ts = dr.ResolveTypeSig(pid); r = ts.GetType().Name + " " + ts.FullName + " elem=" + (ts is SZArraySig sz ? sz.Next.GetType().Name + " " + sz.Next.FullName : "-"); }
                        catch (Exception ex) { r = "<err " + ex.Message + ">"; }
                        Console.WriteLine("    paramTypeId=" + pid + " -> " + r);
                    }
                }
                else if (rec.Category == RefCategory.Type) { declName = rec.Type.Name; Console.WriteLine("  type name=" + Esc(rec.Type.Name)); }
            }
            catch (Exception e) { Console.WriteLine("  (not a ref record: " + e.Message + ")"); }

            try
            {
                MethodSignature sig = new MethodRecordReader(MethodStore.OpenStore(store), false).ReadSignatureRecord(id);
                Console.WriteLine("  [as sigrecord] name=" + Esc(sig.Name) + " declTypeId=" + sig.DeclaringTypeId + " ret=" + sig.ReturnTypeId + " flags=" + sig.Flags);
                if (declId < 0) declId = sig.DeclaringTypeId;
            }
            catch (Exception e) { Console.WriteLine("  (not a sig record: " + e.Message + ")"); }

            if (declId >= 0)
            {
                ReferenceRecord tr = new RefRecordReader(MethodStore.OpenStore(store)).Read(declId);
                declName = tr.Category == RefCategory.Type ? tr.Type.Name : "<cat=" + tr.Category + ">";
                Console.WriteLine("  declType@" + declId + " name=" + Esc(declName));
            }

            if (declName != null && declName.IndexOf(',') >= 0)
            {
                string typeName = declName.Substring(0, declName.IndexOf(',')).Trim();
                Console.WriteLine("  typeName(escaped)=" + Esc(typeName) + "  module.Find=" + (module.Find(typeName, true)?.MDToken.ToString() ?? "null"));
                foreach (TypeDef t in module.GetTypes())
                {
                    string exact = string.IsNullOrEmpty(t.Namespace) ? t.Name.String : t.Namespace + "." + t.Name;
                    if (exact == typeName)
                    {
                        Console.WriteLine("    EXACT tok=" + t.MDToken + " ns=" + Esc(t.Namespace) + " name=" + Esc(t.Name) + " methods=" + t.Methods.Count);
                    }
                    if (t.ReflectionFullName == typeName)
                    {
                        Console.WriteLine("    reflName tok=" + t.MDToken + " name=" + Esc(t.Name));
                    }
                }
            }
        }

        private static string Esc(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") sb.Append(c >= 0x20 && c < 0x7F ? c.ToString() : "\\u" + ((int)c).ToString("X4"));
            return sb.ToString();
        }
    }

    public static class FindSigCli
    {
        public static void Run(string assemblyPath, string ret, string paramCsv)
        {
            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            string[] wantParams = paramCsv.Length == 0 ? new string[0] : paramCsv.Split(',');
            Console.WriteLine("[findsig] ret=" + ret + " params=(" + paramCsv + ")");
            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef m in type.Methods)
                {
                    if (m.MethodSig == null || m.ReturnType.FullName != ret) continue;
                    if (m.MethodSig.Params.Count != wantParams.Length) continue;
                    bool ok = true;
                    for (int i = 0; i < wantParams.Length; i++)
                    {
                        if (m.MethodSig.Params[i].FullName != wantParams[i]) { ok = false; break; }
                    }
                    if (!ok) continue;
                    System.Text.StringBuilder nm = new System.Text.StringBuilder();
                    foreach (char c in m.Name.String ?? "") nm.Append(c >= 0x20 && c < 0x7F ? c.ToString() : "\\u" + ((int)c).ToString("X4"));
                    Console.WriteLine("  declTok=" + type.MDToken + " methTok=" + m.MDToken + " static=" + m.IsStatic + " name=" + nm);
                }
            }
        }
    }

    public static class DevirtCli
    {
        public static void Run(string storePath, long methodOffset, string assemblyPath, string outPath)
        {
            byte[] store = File.ReadAllBytes(storePath);
            MethodRecord record = new MethodRecordReader(MethodStore.OpenStore(store), false).Read(methodOffset);

            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            DnlibResolver resolver = new DnlibResolver(module, store);

            MethodDef target = Devirtualizer.FindStub(module, resolver, record.Signature);
            Console.WriteLine("[devirt] target " + target.FullName + " tok=" + target.MDToken);

            CilEmitter emitter = new CilEmitter(module, resolver);
            CilBody body = emitter.Emit(record, target);
            target.Body = body;

            PrintBody(target);

            if (!string.IsNullOrEmpty(outPath))
            {
                ModuleWriterOptions options = new ModuleWriterOptions(module);
                options.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
                module.Write(outPath, options);
                Console.WriteLine("[devirt] wrote " + outPath);
            }
        }

        private static void PrintBody(MethodDef method)
        {
            CilBody body = method.Body;
            Console.WriteLine("[body] locals=" + body.Variables.Count + " instrs=" + body.Instructions.Count +
                " eh=" + body.ExceptionHandlers.Count);
            for (int i = 0; i < body.Variables.Count; i++)
            {
                Console.WriteLine("  loc[" + i + "] " + body.Variables[i].Type);
            }
            foreach (Instruction instr in body.Instructions)
            {
                Console.WriteLine("  IL_" + instr.Offset.ToString("X4") + "  " +
                    instr.OpCode.Name.PadRight(14) + " " + FormatOperand(instr.Operand) + DescribeScope(instr.Operand));
            }
        }

        private static string DescribeScope(object operand)
        {
            ITypeDefOrRef declType = null;
            if (operand is IMethod method)
            {
                declType = method.DeclaringType;
            }
            else if (operand is IField field)
            {
                declType = field.DeclaringType;
            }
            else if (operand is ITypeDefOrRef t)
            {
                declType = t;
            }
            if (declType == null)
            {
                return "";
            }
            string kind = declType is TypeDef ? "TypeDef" : declType is dnlib.DotNet.TypeRef tr ? "TypeRef:" + tr.Scope : declType.GetType().Name;
            return "   [" + kind + " tok=" + declType.MDToken + "]";
        }

        private static string FormatOperand(object operand)
        {
            if (operand == null)
            {
                return "";
            }
            if (operand is Instruction target)
            {
                return "IL_" + target.Offset.ToString("X4");
            }
            if (operand is Instruction[] targets)
            {
                return "[" + string.Join(", ", targets.Select(t => "IL_" + t.Offset.ToString("X4"))) + "]";
            }
            return operand.ToString();
        }
    }
}
