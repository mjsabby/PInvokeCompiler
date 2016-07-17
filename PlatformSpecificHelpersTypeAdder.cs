namespace PInvokeCompiler
{
    using System.Collections.Generic;
    using Microsoft.Cci;
    using Microsoft.Cci.MutableCodeModel;

    internal sealed class PlatformSpecificHelpersTypeAdder
    {
        public PlatformSpecificHelpersTypeAdder(IMetadataHost host, ITypeDefinition typeDef, ITypeReference marshalClass)
        {
            var libc = new ModuleReference
            {
                ModuleIdentity = new ModuleIdentity(host.NameTable.GetNameFor("libc"), "unknown://location")
            };

            var libdl = new ModuleReference
            {
                ModuleIdentity = new ModuleIdentity(host.NameTable.GetNameFor("libdl"), "unknown://location")
            };

            var libSystem = new ModuleReference
            {
                ModuleIdentity = new ModuleIdentity(host.NameTable.GetNameFor("libSystem"), "unknown://location")
            };

            var kernel32 = new ModuleReference
            {
                ModuleIdentity = new ModuleIdentity(host.NameTable.GetNameFor("kernel32"), "unknown://location")
            };
            
            var linux = CreateUnixSpecificHelpers(host, typeDef, "linux_", "dlopen", "dlclose", "dlsym", libdl, PInvokeCallingConvention.CDecl);
            var darwin = CreateUnixSpecificHelpers(host, typeDef, "darwin_", "dlopen", "dlclose", "dlsym", libSystem, PInvokeCallingConvention.CDecl);
            var bsd = CreateUnixSpecificHelpers(host, typeDef, "bsd_", "dlopen", "dlclose", "dlsym", libc, PInvokeCallingConvention.CDecl);
            var windows = CreateWindowsHelpers(host, typeDef, "windows_", "LoadLibrary", "FreeLibrary", "GetProcAddress", kernel32, PInvokeCallingConvention.WinApi);
            var unix = CreateUnixHelpers(host, typeDef, marshalClass, linux, darwin, bsd, libc);

            this.WindowsLoaderMethods = new WindowsLoaderMethods
            {
                LoadLibrary = windows[0],
                FreeLibrary = windows[1],
                GetProcAddress = windows[2]
            };

            this.UnixLoaderMethods = new UnixLoaderMethods
            {
                LoadLibrary = unix[0],
                FreeLibrary = unix[1],
                GetProcAddress = unix[2]
            };

            this.Methods = new List<IMethodDefinition>();
            this.Methods.AddRange(linux);
            this.Methods.AddRange(darwin);
            this.Methods.AddRange(bsd);
            this.Methods.AddRange(windows);
            this.Methods.AddRange(unix);
        }

        public IWindowsLoaderMethods WindowsLoaderMethods { get; }

        public IUnixLoaderMethods UnixLoaderMethods { get; }

        public List<IMethodDefinition> Methods { get; }

        private static List<IMethodDefinition> CreateWindowsHelpers(
            IMetadataHost host,
            ITypeDefinition typeDef,
            string prefix,
            string loadLibraryMethodName,
            string freeLibraryMethodName,
            string getProcAddressMethodName,
            IModuleReference moduleRef,
            PInvokeCallingConvention callingConvention)
        {
            var loadLibrary = CreateLoadLibraryMethod(host, typeDef, moduleRef, prefix, loadLibraryMethodName, callingConvention);
            var freeLibrary = CreateFreeLibraryMethod(host, typeDef, moduleRef, prefix, freeLibraryMethodName, callingConvention);
            var getProcAddress = CreateGetProcAddressMethod(host, typeDef, moduleRef, prefix, getProcAddressMethodName, callingConvention);
            return new List<IMethodDefinition> { loadLibrary, freeLibrary, getProcAddress };
        }

        private static List<IMethodDefinition> CreateUnixSpecificHelpers(
            IMetadataHost host,
            ITypeDefinition typeDef,
            string prefix,
            string loadLibraryMethodName,
            string freeLibraryMethodName,
            string getProcAddressMethodName,
            IModuleReference moduleRef,
            PInvokeCallingConvention callingConvention)
        {
            var loadLibrary = CreateLoadLibraryMethod(host, typeDef, moduleRef, prefix, loadLibraryMethodName, callingConvention);
            loadLibrary.Parameters.Add(new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemInt32 });
            var freeLibrary = CreateFreeLibraryMethod(host, typeDef, moduleRef, prefix, freeLibraryMethodName, callingConvention);
            var getProcAddress = CreateGetProcAddressMethod(host, typeDef, moduleRef, prefix, getProcAddressMethodName, callingConvention);
            return new List<IMethodDefinition> { loadLibrary, freeLibrary, getProcAddress };
        }

        private static MethodDefinition CreateLoadLibraryMethod(IMetadataHost host, ITypeDefinition typeDef, IModuleReference moduleRef, string prefix, string loadLibraryMethodName, PInvokeCallingConvention callingConvention)
        {
            return CreatePInvokeMethod(
                host,
                typeDef,
                new List<IParameterDefinition>
                {
                    new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString }
                },
                host.PlatformType.SystemIntPtr,
                moduleRef,
                prefix,
                loadLibraryMethodName,
                callingConvention);
        }

        private static MethodDefinition CreateFreeLibraryMethod(IMetadataHost host, ITypeDefinition typeDef, IModuleReference moduleRef, string prefix, string freeLibraryMethodName, PInvokeCallingConvention callingConvention)
        {
            return CreatePInvokeMethod(
                host,
                typeDef,
                new List<IParameterDefinition>
                {
                    new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }
                },
                host.PlatformType.SystemInt32,
                moduleRef,
                prefix,
                freeLibraryMethodName,
                callingConvention);
        }

        private static MethodDefinition CreateGetProcAddressMethod(IMetadataHost host, ITypeDefinition typeDef, IModuleReference moduleRef, string prefix, string getProcAddressMethodName, PInvokeCallingConvention callingConvention)
        {
            return CreatePInvokeMethod(
                host,
                typeDef,
                new List<IParameterDefinition>
                {
                    new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr },
                    new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemString }
                },
                host.PlatformType.SystemIntPtr,
                moduleRef,
                prefix,
                getProcAddressMethodName,
                callingConvention);
        }

        private static MethodDefinition CreatePInvokeMethod(IMetadataHost host, ITypeDefinition typeDef, List<IParameterDefinition> parameters, ITypeReference returnType, IModuleReference moduleRef, string prefix, string methodName, PInvokeCallingConvention callingConvention)
        {
            return new MethodDefinition
            {
                ContainingTypeDefinition = typeDef,
                Type = returnType,
                Parameters = parameters,
                Name = host.NameTable.GetNameFor(prefix + methodName),
                Visibility = TypeMemberVisibility.Public,
                IsStatic = true,
                IsPlatformInvoke = true,
                PreserveSignature = true,
                IsHiddenBySignature = true,
                IsExternal = true,
                PlatformInvokeData = new PlatformInvokeInformation
                {
                    PInvokeCallingConvention = callingConvention,
                    ImportModule = moduleRef,
                    ImportName = host.NameTable.GetNameFor(methodName)
                }
            };
        }

        private static IMethodDefinition CreateGetOperatingSystemMethod(IMetadataHost host, ITypeDefinition typeDef, IMethodReference stringOPEquality, IMethodReference uname)
        {
            var methodDefinition = new MethodDefinition
            {
                Name = host.NameTable.GetNameFor("GetOperatingSystem"),
                IsStatic = true,
                ContainingTypeDefinition = typeDef,
                IsHiddenBySignature = true,
                Visibility = TypeMemberVisibility.Private,
                Type = host.PlatformType.SystemInt32
            };

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

            ilGenerator.Emit(OperationCode.Br_S, unknownLabel);

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
            ilGenerator.Emit(OperationCode.Brtrue_S, operatingSystemSwitchLabel);
        }

        private static IMethodDefinition CreateUnameMethod(IMetadataHost host, ITypeDefinition typeDef, IFieldReference intPtrZero, IMethodReference allocHGlobal, IMethodReference ptrToStringAnsi, IMethodReference freeHGlobal, IMethodReference unamePInvoke, IMethodReference intPtrOpInequality)
        {
            var methodDefinition = new MethodDefinition
            {
                Name = host.NameTable.GetNameFor("uname"),
                IsStatic = true,
                IsHiddenBySignature = true,
                ContainingTypeDefinition = typeDef,
                Visibility = TypeMemberVisibility.Private,
                Type = host.PlatformType.SystemString
            };

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
            ilGenerator.MarkLabel(emptyStringLabel);
            ilGenerator.Emit(OperationCode.Ldstr, "");
            ilGenerator.Emit(OperationCode.Stloc_1);
            ilGenerator.Emit(OperationCode.Leave_S, exitLabel);
            ilGenerator.BeginFinallyBlock();
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Ldsfld, intPtrZero);
            ilGenerator.Emit(OperationCode.Call, intPtrOpInequality);
            ilGenerator.Emit(OperationCode.Brfalse_S, endFinallyLabel);
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Call, freeHGlobal);
            ilGenerator.MarkLabel(endFinallyLabel);
            ilGenerator.Emit(OperationCode.Endfinally);
            ilGenerator.EndTryBody();
            ilGenerator.MarkLabel(exitLabel);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 2, methodDefinition, new List<ILocalDefinition> { new LocalDefinition { Type = host.PlatformType.SystemIntPtr }, new LocalDefinition { Type = host.PlatformType.SystemString } }, new List<ITypeDefinition>());
            return methodDefinition;
        }

        private static IMethodDefinition CreateUnamePInvokeMethod(IMetadataHost host, ITypeDefinition typeDef, IModuleReference libc)
        {
            return CreatePInvokeMethod(host, typeDef, new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr } }, host.PlatformType.SystemInt32, libc, string.Empty, "uname", PInvokeCallingConvention.CDecl);
        }

        private static IMethodDefinition CreateDLOpen(IMetadataHost host, ITypeDefinition typeDef, IMethodReference getOperatingSystem, IMethodReference linuxHelpers, IMethodReference darwinHelpers, IMethodReference bsdHelpers)
        {
            return CreateDLMethod(host, typeDef, host.NameTable.GetNameFor("dlopen"), new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemInt32 } }, host.PlatformType.SystemIntPtr, getOperatingSystem, linuxHelpers, darwinHelpers, bsdHelpers);
        }

        private static IMethodDefinition CreateDLClose(IMetadataHost host, ITypeDefinition typeDef, IMethodReference getOperatingSystem, IMethodReference linuxHelpers, IMethodReference darwinHelpers, IMethodReference bsdHelpers)
        {
            return CreateDLMethod(host, typeDef, host.NameTable.GetNameFor("dlclose"), new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemInt32 } }, host.PlatformType.SystemInt32, getOperatingSystem, linuxHelpers, darwinHelpers, bsdHelpers, generateSecondLoad: false);
        }

        private static IMethodDefinition CreateDLSym(IMetadataHost host, ITypeDefinition typeDef, IMethodReference getOperatingSystem, IMethodReference linuxHelpers, IMethodReference darwinHelpers, IMethodReference bsdHelpers)
        {
            return CreateDLMethod(host, typeDef, host.NameTable.GetNameFor("dlsym"), new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemString } }, host.PlatformType.SystemIntPtr, getOperatingSystem, linuxHelpers, darwinHelpers, bsdHelpers);
        }

        private static IMethodDefinition CreateDLMethod(IMetadataHost host, ITypeDefinition typeDef, IName name, List<IParameterDefinition> parameters, ITypeReference returnType, IMethodReference getOperatingSystem, IMethodReference linuxHelpers, IMethodReference darwinHelpers, IMethodReference bsdHelpers, bool generateSecondLoad = true)
        {
            var methodDefinition = new MethodDefinition
            {
                Name = name,
                Parameters = parameters,
                ContainingTypeDefinition = typeDef,
                IsStatic = true,
                IsHiddenBySignature = true,
                Visibility = TypeMemberVisibility.Public,
                Type = returnType
            };

            var labels = new ILGeneratorLabel[4];

            var linuxLabel = new ILGeneratorLabel();
            var darwinLabel = new ILGeneratorLabel();
            var bsdLabel = new ILGeneratorLabel();
            var unknownLabel = new ILGeneratorLabel();

            labels[0] = linuxLabel;
            labels[1] = darwinLabel;
            labels[2] = bsdLabel;
            labels[3] = bsdLabel;

            var ilGenerator = new ILGenerator(host, methodDefinition);
            ilGenerator.Emit(OperationCode.Call, getOperatingSystem);
            ilGenerator.Emit(OperationCode.Stloc_0);
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Sub);
            ilGenerator.Emit(OperationCode.Switch, labels);
            ilGenerator.Emit(OperationCode.Br_S, unknownLabel);

            AddOperatingSystemCase2(ilGenerator, linuxHelpers, linuxLabel, generateSecondLoad);
            AddOperatingSystemCase2(ilGenerator, darwinHelpers, darwinLabel, generateSecondLoad);
            AddOperatingSystemCase2(ilGenerator, bsdHelpers, bsdLabel, generateSecondLoad);

            ilGenerator.MarkLabel(unknownLabel);
            ilGenerator.Emit(OperationCode.Ldstr, "Platform Not Supported");

            var exceptionCtor = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor(".ctor"),
                ContainingType = host.PlatformType.SystemException,
                Type = host.PlatformType.SystemVoid,
                CallingConvention = CallingConvention.HasThis,
                Parameters = new List<IParameterTypeInformation> {  new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString } }
            };

            ilGenerator.Emit(OperationCode.Newobj, exceptionCtor);
            ilGenerator.Emit(OperationCode.Throw);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 2, methodDefinition, new List<ILocalDefinition> { new LocalDefinition { Type = host.PlatformType.SystemInt32 } }, new List<ITypeDefinition>());
            return methodDefinition;
        }

        private static void AddOperatingSystemCase2(ILGenerator ilGenerator, IMethodReference helper, ILGeneratorLabel label, bool generateSecondLoad = true)
        {
            ilGenerator.MarkLabel(label);
            ilGenerator.Emit(OperationCode.Ldarg_0);

            if (generateSecondLoad)
            {
                ilGenerator.Emit(OperationCode.Ldarg_1);
            }
            
            ilGenerator.Emit(OperationCode.Call, helper);
            ilGenerator.Emit(OperationCode.Ret);
        }

        private static List<IMethodDefinition> CreateUnixHelpers(IMetadataHost host, ITypeDefinition typeDef, ITypeReference marshalClass, List<IMethodDefinition> linuxMethodList, List<IMethodDefinition> darwinMethodList, List<IMethodDefinition> bsdMethodList, IModuleReference libc)
        {
            var intPtrZero = new FieldReference
            {
                Name = host.NameTable.GetNameFor("Zero"),
                ContainingType = host.PlatformType.SystemIntPtr,
                Type = host.PlatformType.SystemIntPtr
            };

            var allocalHGlobal = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("AllocHGlobal"),
                ContainingType = marshalClass,
                Type = host.PlatformType.SystemIntPtr,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemInt32 } }
            };

            var ptrToStringAnsi = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("PtrToStringAnsi"),
                ContainingType = marshalClass,
                Type = host.PlatformType.SystemString,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr } }
            };

            var freeHGlobal = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("FreeHGlobal"),
                ContainingType = marshalClass,
                Type = host.PlatformType.SystemVoid,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr } }
            };

            var intPtrOpInEquality = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.OpInequality,
                ContainingType = host.PlatformType.SystemIntPtr,
                Type = host.PlatformType.SystemBoolean,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemIntPtr } }
            };

            var stringOpEquality = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.OpEquality,
                ContainingType = host.PlatformType.SystemString,
                Type = host.PlatformType.SystemBoolean,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemString } }
            };

            var unamePInvokeMethod = CreateUnamePInvokeMethod(host, typeDef, libc);
            var unameMethod = CreateUnameMethod(host, typeDef, intPtrZero, allocalHGlobal, ptrToStringAnsi, freeHGlobal, unamePInvokeMethod, intPtrOpInEquality);
            var getosmethod = CreateGetOperatingSystemMethod(host, typeDef, stringOpEquality, unameMethod);
            
            var dlopen = CreateDLOpen(host, typeDef, getosmethod, linuxMethodList[0], darwinMethodList[0], bsdMethodList[0]);
            var dlclose = CreateDLClose(host, typeDef, getosmethod, linuxMethodList[1], darwinMethodList[1], bsdMethodList[1]);
            var dlsym = CreateDLSym(host, typeDef, getosmethod, linuxMethodList[2], darwinMethodList[2], bsdMethodList[2]);
            
            return new List<IMethodDefinition> { dlopen, dlclose, dlsym, unameMethod, unamePInvokeMethod, getosmethod };
        }
    }
}