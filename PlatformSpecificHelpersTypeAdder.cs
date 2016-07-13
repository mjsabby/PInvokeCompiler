namespace PInvokeCompiler
{
    using System.Collections.Generic;
    using Microsoft.Cci;
    using Microsoft.Cci.MutableCodeModel;

    internal sealed class PlatformSpecificHelpersTypeAdder : MetadataRewriter
    {
        public PlatformSpecificHelpersTypeAdder(IMetadataHost host, bool copyAndRewriteImmutableReferences = false)
            : base(host, copyAndRewriteImmutableReferences)
        {
        }

        public override void RewriteChildren(Assembly module)
        {
            var typeDefList = new List<INamedTypeDefinition>();
            var rootUnitNamespace = new RootUnitNamespace();
            rootUnitNamespace.Members.AddRange(module.UnitNamespaceRoot.Members);
            module.UnitNamespaceRoot = rootUnitNamespace;
            rootUnitNamespace.Unit = module;

            typeDefList.Add(CreateOSTypeEnum(host, rootUnitNamespace));
            typeDefList.Add(CreatePlatformSpecificHelpers(this.host, rootUnitNamespace, "WindowsHelpers", "LoadLibrary", "FreeLibrary", "GetProcAddress", "kernel32", PInvokeCallingConvention.StdCall, isUnix: false));
            typeDefList.Add(CreatePlatformSpecificHelpers(this.host, rootUnitNamespace, "DarwinHelpers", "dlopen", "dlclose", "dlsym", "libSystem", PInvokeCallingConvention.StdCall));
            typeDefList.Add(CreatePlatformSpecificHelpers(this.host, rootUnitNamespace, "LinuxHelpers", "dlopen", "dlclose", "dlsym", "libdl", PInvokeCallingConvention.StdCall));
            typeDefList.Add(CreatePlatformSpecificHelpers(this.host, rootUnitNamespace, "BSDHelpers", "dlopen", "dlclose", "dlsym", "libc", PInvokeCallingConvention.StdCall));

            foreach (var t in typeDefList)
            {
                rootUnitNamespace.Members.Add((INamespaceMember)t);
                module.AllTypes.Add(t);
            }
            
            base.RewriteChildren(module);
        }

        private static INamedTypeDefinition CreatePlatformSpecificHelpers(
            IMetadataHost host,
            IRootUnitNamespace rootUnitNamespace,
            string className,
            string loadLibraryMethodName,
            string freeLibraryMethodName,
            string getProcAddressMethodName,
            string moduleRef,
            PInvokeCallingConvention callingConvention,
            bool isUnix = true)
        {
            var typeDef = new NamespaceTypeDefinition();

            var loadLibrary = CreateLoadLibraryMethod(host, typeDef, moduleRef, loadLibraryMethodName, callingConvention);

            if (isUnix)
            {
                loadLibrary.Parameters.Add(new ParameterDefinition { Type = host.PlatformType.SystemInt32 });
            }

            var freeLibrary = CreateFreeLibraryMethod(host, typeDef, moduleRef, freeLibraryMethodName, callingConvention);
            var getProcAddress = CreateGetProcAddressMethod(host, typeDef, moduleRef, getProcAddressMethodName, callingConvention);

            typeDef.ContainingUnitNamespace = rootUnitNamespace;
            typeDef.Methods = new List<IMethodDefinition> { loadLibrary, freeLibrary, getProcAddress };
            typeDef.IsPublic = true;
            typeDef.BaseClasses = new List<ITypeReference> { host.PlatformType.SystemObject };
            typeDef.Name = host.NameTable.GetNameFor(className);

            return typeDef;
        }

        private static MethodDefinition CreateLoadLibraryMethod(IMetadataHost host, INamedTypeDefinition typeDef, string moduleRefName, string loadLibraryMethodName, PInvokeCallingConvention callingConvention)
        {
            return CreatePInvokeMethod(
                host,
                typeDef,
                new List<IParameterDefinition>
                {
                    new ParameterDefinition { Type = host.PlatformType.SystemString }
                },
                host.PlatformType.SystemIntPtr,
                moduleRefName,
                loadLibraryMethodName,
                callingConvention);
        }

        private static MethodDefinition CreateFreeLibraryMethod(IMetadataHost host, INamedTypeDefinition typeDef, string moduleRefName, string freeLibraryMethodName, PInvokeCallingConvention callingConvention)
        {
            return CreatePInvokeMethod(
                host,
                typeDef,
                new List<IParameterDefinition>
                {
                    new ParameterDefinition { Type = host.PlatformType.SystemIntPtr }
                },
                host.PlatformType.SystemInt32,
                moduleRefName,
                freeLibraryMethodName,
                callingConvention);
        }

        private static MethodDefinition CreateGetProcAddressMethod(IMetadataHost host, INamedTypeDefinition typeDef, string moduleRefName, string getProcAddressMethodName, PInvokeCallingConvention callingConvention)
        {
            return CreatePInvokeMethod(
                host,
                typeDef,
                new List<IParameterDefinition>
                {
                    new ParameterDefinition { Type = host.PlatformType.SystemIntPtr },
                    new ParameterDefinition { Type = host.PlatformType.SystemString }
                },
                host.PlatformType.SystemIntPtr,
                moduleRefName,
                getProcAddressMethodName,
                callingConvention);
        }

        private static MethodDefinition CreatePInvokeMethod(IMetadataHost host, INamedTypeDefinition typeDef, List<IParameterDefinition> parameters, ITypeReference returnType, string moduleRefName, string methodName, PInvokeCallingConvention callingConvention)
        {
            var exportMethodName = host.NameTable.GetNameFor(methodName);

            return new MethodDefinition
            {
                ContainingTypeDefinition = typeDef,
                Type = returnType,
                Parameters = parameters,
                Name = exportMethodName,
                Visibility = TypeMemberVisibility.Assembly,
                IsStatic = true,
                IsPlatformInvoke = true,
                IsHiddenBySignature = true,
                IsExternal = true,
                PlatformInvokeData = new PlatformInvokeInformation
                {
                    PInvokeCallingConvention = callingConvention,
                    ImportModule = new ModuleReference { ModuleIdentity = new ModuleIdentity(host.NameTable.GetNameFor(moduleRefName), "unknown://location") },
                    ImportName = exportMethodName
                }
            };
        }

        private static INamedTypeDefinition CreateOSTypeEnum(IMetadataHost host, IRootUnitNamespace rootUnitNamespace)
        {
            var typeDef = new NamespaceTypeDefinition
            {
                ContainingUnitNamespace = rootUnitNamespace,
                IsPublic = true,
                IsValueType = true,
                IsSealed = true,
                BaseClasses = new List<ITypeReference> {host.PlatformType.SystemEnum},
                Name = host.NameTable.GetNameFor("OSType")
            };

            typeDef.Fields = new List<IFieldDefinition>
            {
                CreateStaticEnumField(host, typeDef, "Unknown", 0),
                CreateStaticEnumField(host, typeDef, "Linux", 1),
                CreateStaticEnumField(host, typeDef, "MacOSX", 2),
                CreateStaticEnumField(host, typeDef, "FreeBSD", 3),
                CreateStaticEnumField(host, typeDef, "NetBSD", 4)
            };

            return typeDef;
        }

        private static IFieldDefinition CreateStaticEnumField(IMetadataHost host, ITypeReference type, string name, int value)
        {
            var constant = new MetadataConstant
            {
                Value = value,
                Type = host.PlatformType.SystemInt32
            };

            return new FieldDefinition
            {
                Name = host.NameTable.GetNameFor(name),
                IsStatic = true,
                Visibility = TypeMemberVisibility.Assembly,
                Type = type,
                
            };
        }

        private static IMethodDefinition CreateGetOperatingSystemMethod(IMetadataHost host, IMethodReference stringOPEquality, IMethodReference uname)
        {
            var methodDefinition = new MethodDefinition();
            ILGenerator ilGenerator = new ILGenerator(host, methodDefinition);
            ilGenerator.Emit(OperationCode.Call, uname);
            ilGenerator.Emit(OperationCode.Stloc_0);

            var linuxLabel = new ILGeneratorLabel();
            var darwinLabel = new ILGeneratorLabel();
            var freebsdLabel = new ILGeneratorLabel();
            var netbsdLabel = new ILGeneratorLabel();
            var unknownLabel = new ILGeneratorLabel();

            AddOperatingSystemCase(ilGenerator, "Linux", stringOPEquality, linuxLabel);
            AddOperatingSystemCase(ilGenerator, "Darwin", stringOPEquality, darwinLabel);
            AddOperatingSystemCase(ilGenerator, "FreeBSD", stringOPEquality, freebsdLabel);
            AddOperatingSystemCase(ilGenerator, "NetBSD", stringOPEquality, netbsdLabel);

            ilGenerator.Emit(OperationCode.Br, unknownLabel);

            ilGenerator.MarkLabel(linuxLabel);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Ret);

            ilGenerator.MarkLabel(darwinLabel);
            ilGenerator.Emit(OperationCode.Ldc_I4_2);
            ilGenerator.Emit(OperationCode.Ret);

            ilGenerator.MarkLabel(freebsdLabel);
            ilGenerator.Emit(OperationCode.Ldc_I4_3);
            ilGenerator.Emit(OperationCode.Ret);

            ilGenerator.MarkLabel(netbsdLabel);
            ilGenerator.Emit(OperationCode.Ldc_I4_4);
            ilGenerator.Emit(OperationCode.Ret);

            ilGenerator.MarkLabel(unknownLabel);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 2, methodDefinition, new List<ILocalDefinition> { new LocalDefinition { Type = host.PlatformType.SystemString } }, new List<ITypeDefinition>());
            return methodDefinition;
        }

        private static void AddOperatingSystemCase(ILGenerator ilGenerator, string operatingSystemName, IMethodReference stringOPEquality, ILGeneratorLabel operatingSystemSwitchLabel)
        {
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Ldstr, operatingSystemName);
            ilGenerator.Emit(OperationCode.Call, stringOPEquality);
            ilGenerator.Emit(OperationCode.Brtrue, operatingSystemSwitchLabel);
        }

        private static void CreateUnameMethod(IMetadataHost host, IFieldReference intPtrZero, IMethodReference allocHGlobal, IMethodReference ptrToStringAnsi, IMethodReference freeHGlobal, IMethodReference unamePInvoke, IMethodReference intPtrOpInequality)
        {
            var methodDefinition = new MethodDefinition();
            var ilGenerator = new ILGenerator(host, methodDefinition);
            ilGenerator.Emit(OperationCode.Ldsfld, intPtrZero);
            ilGenerator.Emit(OperationCode.Stloc_0);
            ilGenerator.BeginTryBody();
            ilGenerator.Emit(OperationCode.Ldc_I4, 0xff);
            ilGenerator.Emit(OperationCode.Call, allocHGlobal);
            ilGenerator.Emit(OperationCode.Stloc_0);
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Call, unamePInvoke);

            var emptyStringLabel = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Brtrue_S, emptyStringLabel);
            ilGenerator.Emit(OperationCode.Ldloc_0);

            ilGenerator.Emit(OperationCode.Call, ptrToStringAnsi);
            ilGenerator.Emit(OperationCode.Stloc_1);
            
            var exitLabel = new ILGeneratorLabel();
            var endFinallyLabel = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Leave_S, exitLabel);
            ilGenerator.Emit(OperationCode.Ldstr, "");
            ilGenerator.Emit(OperationCode.Stloc_1);
            ilGenerator.Emit(OperationCode.Leave_S, exitLabel);
            ilGenerator.EndTryBody();
            ilGenerator.BeginFinallyBlock();
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Ldsfld, intPtrZero);
            ilGenerator.Emit(OperationCode.Call, intPtrOpInequality);
            ilGenerator.Emit(OperationCode.Brfalse_S, endFinallyLabel);
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Call, freeHGlobal);
            ilGenerator.MarkLabel(endFinallyLabel);
            ilGenerator.Emit(OperationCode.Endfinally);
            ilGenerator.MarkLabel(exitLabel);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ret);

            ilGenerator.Emit();
        }

        private static void CreateUnamePInvokeMethod(IMetadataHost host, INamedTypeDefinition typeDef)
        {
            CreatePInvokeMethod(host, typeDef, new List<IParameterDefinition> { new ParameterDefinition { Type = host.PlatformType.SystemIntPtr } }, host.PlatformType.SystemInt32, "libc", "uname", PInvokeCallingConvention.CDecl);
        }
    }
}