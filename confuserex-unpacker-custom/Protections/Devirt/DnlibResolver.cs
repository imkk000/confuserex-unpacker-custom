using System;
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet;

namespace Protections.Devirt
{
    public sealed class DnlibResolver
    {
        private readonly ModuleDefMD module;
        private readonly RefRecordReader refReader;
        private readonly MethodRecordReader methodReader;
        private readonly Dictionary<long, TypeSig> typeSigCache = new Dictionary<long, TypeSig>();
        private Dictionary<string, TypeDef> exactTypes;

        public DnlibResolver(ModuleDefMD module, byte[] decryptedStore)
        {
            this.module = module;
            this.refReader = new RefRecordReader(MethodStore.OpenStore(decryptedStore));
            this.methodReader = new MethodRecordReader(MethodStore.OpenStore(decryptedStore), false);
        }

        public TypeSig ResolveTypeSig(long id)
        {
            if (this.typeSigCache.TryGetValue(id, out TypeSig cached))
            {
                return cached;
            }
            ReferenceRecord record = this.refReader.Read(id);
            TypeSig sig;
            if (record.Category == RefCategory.RawToken)
            {
                ITypeDefOrRef tdr = this.module.ResolveToken(record.RawToken) as ITypeDefOrRef;
                if (tdr == null)
                {
                    throw new InvalidOperationException("token 0x" + record.RawToken.ToString("X8") + " is not a type at id " + id);
                }
                sig = tdr.ToTypeSig();
            }
            else if (record.Category == RefCategory.Type)
            {
                sig = BuildTypeSig(record.Type);
            }
            else
            {
                throw new InvalidOperationException("reference at id " + id + " is " + record.Category + ", not a type");
            }
            this.typeSigCache[id] = sig;
            return sig;
        }

        private TypeSig BuildTypeSig(TypeRef t)
        {
            if (t.IsGenericParam)
            {
                return t.MethodGenericIndex != -1
                    ? new GenericMVar((uint)t.MethodGenericIndex)
                    : new GenericVar((uint)t.TypeGenericIndex);
            }

            int comma = t.Name.IndexOf(',');
            string typeNamePart = comma >= 0 ? t.Name.Substring(0, comma) : t.Name;
            string assemblyPart = comma >= 0 ? t.Name.Substring(comma) : "";
            int arrayRank = 0;
            string baseTypeName = StripArraySuffix(typeNamePart, out arrayRank);
            ITypeDefOrRef baseType = ResolveNamedType(baseTypeName + assemblyPart);
            TypeSig sig;
            if (t.IsGenericInstance && t.GenericArgumentIds.Count > 0)
            {
                ClassOrValueTypeSig genBase = ToClassOrValueTypeSig(baseType);
                List<TypeSig> args = new List<TypeSig>();
                foreach (long argId in t.GenericArgumentIds)
                {
                    args.Add(ResolveTypeSig(argId));
                }
                sig = new GenericInstSig(genBase, args);
            }
            else
            {
                sig = baseType.ToTypeSig();
            }

            for (int i = 0; i < arrayRank; i++)
            {
                sig = new SZArraySig(sig);
            }
            return sig;
        }

        private static string StripArraySuffix(string name, out int rank)
        {
            rank = 0;
            while (name.EndsWith("[]", StringComparison.Ordinal))
            {
                rank++;
                name = name.Substring(0, name.Length - 2);
            }
            return name;
        }

        private ITypeDefOrRef ResolveNamedType(string assemblyQualified)
        {
            int comma = assemblyQualified.IndexOf(',');
            string typeName = comma >= 0 ? assemblyQualified.Substring(0, comma) : assemblyQualified;
            string assemblySpec = comma >= 0 ? assemblyQualified.Substring(comma + 1).Trim() : null;

            string assemblySimpleName = AssemblySimpleName(assemblySpec);
            bool isMebAssembly = assemblySimpleName == null ||
                (this.module.Assembly != null &&
                 string.Equals(assemblySimpleName, this.module.Assembly.Name, StringComparison.OrdinalIgnoreCase));

            if (isMebAssembly)
            {
                TypeDef def = FindExactType(typeName) ?? this.module.Find(typeName, true);
                if (def != null)
                {
                    return def;
                }
            }

            TypeSig primitive = MapPrimitive(typeName);
            if (primitive != null)
            {
                return primitive.ToTypeDefOrRef();
            }

            IResolutionScope scope = ResolveScope(assemblySimpleName);
            return MakeTypeRef(typeName, scope);
        }

        private TypeDef FindExactType(string reflectionName)
        {
            if (this.exactTypes == null)
            {
                this.exactTypes = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
                foreach (TypeDef t in this.module.GetTypes())
                {
                    string name = ExactReflectionName(t);
                    if (!this.exactTypes.ContainsKey(name))
                    {
                        this.exactTypes[name] = t;
                    }
                }
            }
            return this.exactTypes.TryGetValue(reflectionName, out TypeDef def) ? def : null;
        }

        private static string ExactReflectionName(TypeDef type)
        {
            string name = type.Name;
            TypeDef declaring = type.DeclaringType;
            while (declaring != null)
            {
                name = declaring.Name + "+" + name;
                declaring = declaring.DeclaringType;
            }
            TypeDef outer = type;
            while (outer.DeclaringType != null)
            {
                outer = outer.DeclaringType;
            }
            if (!string.IsNullOrEmpty(outer.Namespace))
            {
                name = outer.Namespace + "." + name;
            }
            return name;
        }

        private static string AssemblySimpleName(string assemblySpec)
        {
            if (string.IsNullOrEmpty(assemblySpec))
            {
                return null;
            }
            int comma = assemblySpec.IndexOf(',');
            return (comma >= 0 ? assemblySpec.Substring(0, comma) : assemblySpec).Trim();
        }

        private IResolutionScope ResolveScope(string assemblySimpleName)
        {
            if (assemblySimpleName == null)
            {
                return this.module.CorLibTypes.AssemblyRef;
            }
            foreach (AssemblyRef existing in this.module.GetAssemblyRefs())
            {
                if (string.Equals(existing.Name, assemblySimpleName, StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }
            }
            if (string.Equals(assemblySimpleName, "mscorlib", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assemblySimpleName, "System.Private.CoreLib", StringComparison.OrdinalIgnoreCase))
            {
                return this.module.CorLibTypes.AssemblyRef;
            }
            return new AssemblyRefUser(assemblySimpleName);
        }

        private ITypeDefOrRef MakeTypeRef(string reflectionName, IResolutionScope scope)
        {
            string[] nestedParts = reflectionName.Split('+');
            dnlib.DotNet.TypeRef current = null;
            for (int i = 0; i < nestedParts.Length; i++)
            {
                string part = nestedParts[i];
                if (i == 0)
                {
                    int dot = part.LastIndexOf('.');
                    string ns = dot >= 0 ? part.Substring(0, dot) : "";
                    string nm = dot >= 0 ? part.Substring(dot + 1) : part;
                    current = new TypeRefUser(this.module, ns, nm, scope);
                }
                else
                {
                    current = new TypeRefUser(this.module, "", part, current);
                }
            }
            return current;
        }

        private ClassOrValueTypeSig ToClassOrValueTypeSig(ITypeDefOrRef type)
        {
            TypeDef def = type as TypeDef ?? type.ResolveTypeDef();
            bool isValueType = def != null && def.IsValueType;
            return isValueType
                ? (ClassOrValueTypeSig)new ValueTypeSig(type)
                : new ClassSig(type);
        }

        private TypeSig MapPrimitive(string typeName)
        {
            ICorLibTypes c = this.module.CorLibTypes;
            switch (typeName)
            {
                case "System.Void": return c.Void;
                case "System.Boolean": return c.Boolean;
                case "System.Char": return c.Char;
                case "System.SByte": return c.SByte;
                case "System.Byte": return c.Byte;
                case "System.Int16": return c.Int16;
                case "System.UInt16": return c.UInt16;
                case "System.Int32": return c.Int32;
                case "System.UInt32": return c.UInt32;
                case "System.Int64": return c.Int64;
                case "System.UInt64": return c.UInt64;
                case "System.Single": return c.Single;
                case "System.Double": return c.Double;
                case "System.String": return c.String;
                case "System.Object": return c.Object;
                case "System.IntPtr": return c.IntPtr;
                case "System.UIntPtr": return c.UIntPtr;
                case "System.TypedReference": return c.TypedReference;
                default: return null;
            }
        }

        public IMethod ResolveMethod(long id)
        {
            ReferenceRecord record = this.refReader.Read(id);
            if (record.Category == RefCategory.RawToken)
            {
                IMethod m = this.module.ResolveToken(record.RawToken) as IMethod;
                if (m == null)
                {
                    throw new InvalidOperationException("token 0x" + record.RawToken.ToString("X8") + " is not a method at id " + id);
                }
                return m;
            }
            if (record.Category != RefCategory.Method)
            {
                throw new InvalidOperationException("reference at id " + id + " is " + record.Category + ", not a method");
            }
            return BuildMethod(record.Method);
        }

        private IMethod BuildMethod(MethodRef m)
        {
            ITypeDefOrRef declType = ResolveTypeSig(m.DeclaringTypeId).ToTypeDefOrRef();
            TypeSig returnType = ResolveTypeSig(m.ReturnTypeId);
            TypeSig[] paramTypes = new TypeSig[m.ParameterTypeIds.Count];
            for (int i = 0; i < paramTypes.Length; i++)
            {
                paramTypes[i] = ResolveTypeSig(m.ParameterTypeIds[i]);
            }

            if (!m.IsGenericInstance && declType is TypeDef localDecl)
            {
                MethodDef local = FindLocalMethod(localDecl, m.Name, returnType, paramTypes);
                if (local != null)
                {
                    return local;
                }
            }

            MethodSig sig;
            if (m.IsGenericInstance)
            {
                uint genCount = (uint)m.GenericArgumentIds.Count;
                sig = m.IsStatic
                    ? MethodSig.CreateStaticGeneric(genCount, returnType, paramTypes)
                    : MethodSig.CreateInstanceGeneric(genCount, returnType, paramTypes);
            }
            else
            {
                sig = m.IsStatic
                    ? MethodSig.CreateStatic(returnType, paramTypes)
                    : MethodSig.CreateInstance(returnType, paramTypes);
            }

            MemberRefUser memberRef = new MemberRefUser(this.module, m.Name, sig, declType);
            if (!m.IsGenericInstance)
            {
                return memberRef;
            }

            TypeSig[] genArgs = new TypeSig[m.GenericArgumentIds.Count];
            for (int i = 0; i < genArgs.Length; i++)
            {
                genArgs[i] = ResolveTypeSig(m.GenericArgumentIds[i]);
            }
            return new MethodSpecUser(memberRef, new GenericInstMethodSig(genArgs));
        }

        public IMethod ResolveSignatureMethod(long id)
        {
            MethodSignature s = this.methodReader.ReadSignatureRecord(id);
            bool isStatic = (s.Flags & 2) != 0;
            ITypeDefOrRef declType = ResolveTypeSig(s.DeclaringTypeId).ToTypeDefOrRef();
            TypeSig returnType = ResolveTypeSig(s.ReturnTypeId);

            int start = isStatic ? 0 : 1;
            List<TypeSig> paramTypes = new List<TypeSig>();
            for (int i = start; i < s.Parameters.Count; i++)
            {
                ParamEntry p = s.Parameters[i];
                TypeSig pt = ResolveTypeSig(p.TypeId);
                if (p.ByRef)
                {
                    pt = new ByRefSig(pt);
                }
                paramTypes.Add(pt);
            }

            if (declType is TypeDef localDecl)
            {
                MethodDef local = FindLocalMethod(localDecl, s.Name, returnType, paramTypes.ToArray());
                if (local != null)
                {
                    return local;
                }
            }

            MethodSig sig = isStatic
                ? MethodSig.CreateStatic(returnType, paramTypes.ToArray())
                : MethodSig.CreateInstance(returnType, paramTypes.ToArray());
            return new MemberRefUser(this.module, s.Name, sig, declType);
        }

        private static MethodDef FindLocalMethod(TypeDef type, string name, TypeSig returnType, IList<TypeSig> paramTypes)
        {
            foreach (MethodDef md in type.Methods)
            {
                if (md.Name != name || md.MethodSig == null)
                {
                    continue;
                }
                if (md.MethodSig.Params.Count != paramTypes.Count)
                {
                    continue;
                }
                if (md.ReturnType.FullName != returnType.FullName)
                {
                    continue;
                }
                bool match = true;
                for (int i = 0; i < paramTypes.Count; i++)
                {
                    if (md.MethodSig.Params[i].FullName != paramTypes[i].FullName)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return md;
                }
            }
            return null;
        }

        public IField ResolveField(long id)
        {
            ReferenceRecord record = this.refReader.Read(id);
            if (record.Category == RefCategory.RawToken)
            {
                IField f = this.module.ResolveToken(record.RawToken) as IField;
                if (f == null)
                {
                    throw new InvalidOperationException("token 0x" + record.RawToken.ToString("X8") + " is not a field at id " + id);
                }
                return f;
            }
            if (record.Category != RefCategory.Field)
            {
                throw new InvalidOperationException("reference at id " + id + " is " + record.Category + ", not a field");
            }

            FieldRef fr = record.Field;
            ITypeDefOrRef declType = ResolveTypeSig(fr.DeclaringTypeId).ToTypeDefOrRef();
            TypeDef declDef = declType as TypeDef ?? declType.ResolveTypeDef();
            if (declDef == null)
            {
                TypeSig knownType = KnownExternalFieldType(declType.FullName, fr.Name);
                if (knownType != null)
                {
                    return new MemberRefUser(this.module, fr.Name, new FieldSig(knownType), declType);
                }
                throw new NotSupportedException("field " + fr.Name + " on external type " + declType.FullName +
                    " has no serialized field type; cannot build a MemberRef at id " + id);
            }
            FieldDef field = declDef.FindField(fr.Name);
            if (field == null)
            {
                throw new InvalidOperationException("field " + fr.Name + " not found on " + declDef.FullName + " at id " + id);
            }
            return field;
        }

        private TypeSig KnownExternalFieldType(string declTypeFullName, string fieldName)
        {
            if (declTypeFullName == "System.String" && fieldName == "Empty")
            {
                return this.module.CorLibTypes.String;
            }
            return null;
        }

        public string ResolveString(long id)
        {
            ReferenceRecord record = this.refReader.Read(id);
            if (record.Category == RefCategory.String)
            {
                return record.Literal;
            }
            throw new InvalidOperationException("reference at id " + id + " is " + record.Category + ", not a string");
        }

        public ITokenOperand ResolveTokenOperand(long id)
        {
            ReferenceRecord record = this.refReader.Read(id);
            switch (record.Category)
            {
                case RefCategory.RawToken:
                    return this.module.ResolveToken(record.RawToken) as ITokenOperand;
                case RefCategory.Method:
                    return ResolveMethod(id);
                case RefCategory.Field:
                    return ResolveField(id);
                case RefCategory.Type:
                    return ResolveTypeSig(id).ToTypeDefOrRef();
                default:
                    throw new InvalidOperationException("reference at id " + id + " is " + record.Category + ", not a token target");
            }
        }
    }

    public static class DnlibResolveCli
    {
        public static void Dump(string storePath, long methodOffset, string assemblyPath)
        {
            byte[] store = File.ReadAllBytes(storePath);
            StoreReader reader = MethodStore.OpenStore(store);
            MethodRecord rec = new MethodRecordReader(reader, false).Read(methodOffset);
            List<VmInstruction> instrs = BytecodeDecoder.Decode(rec.Bytecode, OpcodeMap.KeyToOperandType());

            ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
            DnlibResolver resolver = new DnlibResolver(module, store);

            Console.WriteLine("[resolvednlib] method@" + methodOffset + " against " + Path.GetFileName(assemblyPath) + ":");
            foreach (VmInstruction instr in instrs)
            {
                OpInfo info = OpcodeMap.ByKey[instr.Key];
                if (!RefResolveCli.TryGetOperandId(instr, info, out long id, out bool isVirtual, out bool isMethodCall))
                {
                    continue;
                }

                string mnem = info.IsUnifiedCall ? "call*" : info.Cil.Name;
                string resolved;
                string declInfo = "";
                try
                {
                    resolved = ResolveForOp(resolver, info, id, isMethodCall);
                    declInfo = DescribeDeclaringScope(resolver, info, id, isMethodCall);
                }
                catch (Exception ex)
                {
                    resolved = "<error " + ex.GetType().Name + ": " + ex.Message + ">";
                }
                Console.WriteLine("IL_" + instr.Ip.ToString("X4") + "  " + mnem.PadRight(14) + " id=" + id + "  => " + resolved + declInfo);
            }
        }

        private static string DescribeDeclaringScope(DnlibResolver resolver, OpInfo info, long id, bool isMethodCall)
        {
            ITypeDefOrRef declType = null;
            if (info.IsUnifiedCall && isMethodCall)
            {
                declType = resolver.ResolveSignatureMethod(id).DeclaringType;
            }
            else if (!info.IsUnifiedCall)
            {
                string kind = ClassifyOperand(info);
                if (kind == "method")
                {
                    declType = resolver.ResolveMethod(id).DeclaringType;
                }
                else if (kind == "field")
                {
                    declType = resolver.ResolveField(id).DeclaringType;
                }
                else if (kind == "type")
                {
                    declType = resolver.ResolveTypeSig(id).ToTypeDefOrRef();
                }
            }
            if (declType == null)
            {
                return "";
            }
            string scope = declType is TypeDef ? "TypeDef(local)" : "TypeRef(external)";
            return "   [" + scope + " tok=" + declType.MDToken + "]";
        }

        private static string ResolveForOp(DnlibResolver resolver, OpInfo info, long id, bool isMethodCall)
        {
            if (info.IsUnifiedCall)
            {
                return isMethodCall
                    ? resolver.ResolveSignatureMethod(id).ToString()
                    : "calli";
            }

            string handlerKind = ClassifyOperand(info);
            switch (handlerKind)
            {
                case "method":
                    return resolver.ResolveMethod(id).ToString();
                case "field":
                    return resolver.ResolveField(id).ToString();
                case "type":
                    return resolver.ResolveTypeSig(id).ToString();
                case "string":
                    return "\"" + resolver.ResolveString(id) + "\"";
                default:
                    return resolver.ResolveMethod(id).ToString();
            }
        }

        private static string ClassifyOperand(OpInfo info)
        {
            string n = info.Cil.Name;
            if (n == "ldstr")
            {
                return "string";
            }
            if (n.StartsWith("ldfld") || n.StartsWith("stfld") || n.StartsWith("ldsfld") ||
                n.StartsWith("stsfld") || n.StartsWith("ldflda") || n.StartsWith("ldsflda"))
            {
                return "field";
            }
            if (n == "newarr" || n == "castclass" || n == "isinst" || n == "box" || n == "unbox" ||
                n == "unbox.any" || n == "initobj" || n == "ldobj" || n == "stobj" || n == "ldtoken" ||
                n == "sizeof" || n == "ldelem" || n == "stelem" || n == "ldelema" || n == "constrained.")
            {
                return "type";
            }
            return "method";
        }
    }
}
