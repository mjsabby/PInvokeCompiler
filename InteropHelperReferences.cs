namespace PInvokeCompiler
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Cci;
    using Microsoft.Cci.MutableCodeModel;

    internal sealed class InteropHelperReferences
    {
        public InteropHelperReferences(IMetadataHost host, Assembly assembly)
        {
            ITypeReference systemEnvironment = null;

            foreach (var typeRef in assembly.ResolvedAssembly.GetTypeReferences())
            {
                var name = TypeHelper.GetTypeName(typeRef);

                if (string.Equals(name, "System.Environment", StringComparison.Ordinal))
                {
                    systemEnvironment = typeRef;
                    continue;
                }
                
                if (string.Equals(name, "System.Runtime.InteropServices.Marshal", StringComparison.Ordinal))
                {
                    this.SystemRuntimeInteropServicesMarshal = typeRef;
                }

                if (string.Equals(name, "System.Runtime.CompilerServices.RuntimeHelpers", StringComparison.Ordinal))
                {
                    this.SystemRuntimeCompilerServicesRuntimeHelpers = typeRef;
                }
            }

            var mscorlibAsmRef = assembly.AssemblyReferences.FirstOrDefault(t => t.Name.Value.Equals("mscorlib"));
            if (mscorlibAsmRef == null)
            {
                var tempRef = new AssemblyReference
                {
                    Name = host.NameTable.GetNameFor("mscorlib"),
                    Version = new Version(4, 0, 0, 0),
                    PublicKeyToken = new List<byte> { 0xB7, 0x7A, 0x5C, 0x56, 0x19, 0x34, 0xE0, 0x89 }
                };

                mscorlibAsmRef = tempRef;
                assembly.AssemblyReferences.Add(mscorlibAsmRef);
            }
            
            if (systemEnvironment == null)
            {
                systemEnvironment = CreateTypeReference(host, mscorlibAsmRef, "System.Environment");
            }
            
            if (this.SystemRuntimeInteropServicesMarshal == null)
            {
                var interopServicesAssembly = assembly.AssemblyReferences.FirstOrDefault(t => t.Name.Value.Equals("System.Runtime.InteropServices")) ?? mscorlibAsmRef;
                this.SystemRuntimeInteropServicesMarshal = CreateTypeReference(host, interopServicesAssembly, "System.Runtime.InteropServices.Marshal");
            }

            if (this.SystemRuntimeCompilerServicesRuntimeHelpers == null)
            {
                var runtimeAssembly = assembly.AssemblyReferences.FirstOrDefault(t => t.Name.Value.Equals("System.Runtime")) ?? mscorlibAsmRef;
                this.SystemRuntimeCompilerServicesRuntimeHelpers = CreateTypeReference(host, runtimeAssembly, "System.Runtime.CompilerServices.RuntimeHelpers");
            }

            var getNewLine = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("get_NewLine"),
                ContainingType = systemEnvironment,
                Type = host.PlatformType.SystemString
            };

            var stringOpEquality = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.OpEquality,
                ContainingType = host.PlatformType.SystemString,
                Type = host.PlatformType.SystemBoolean,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemString } }
            };

            var rootUnitNamespace = new RootUnitNamespace();
            rootUnitNamespace.Members.AddRange(assembly.UnitNamespaceRoot.Members);
            assembly.UnitNamespaceRoot = rootUnitNamespace;
            rootUnitNamespace.Unit = assembly;

            var typeDef = CreatePInvokeHelpers(rootUnitNamespace, host);
            this.PInvokeHelpers = typeDef;
            rootUnitNamespace.Members.Add(typeDef);
            assembly.AllTypes.Add(typeDef);

            var platformSpecificHelpers = new PlatformSpecificHelpersTypeAdder(host, typeDef, SystemRuntimeInteropServicesMarshal);

            var windowsLoaderMethods = platformSpecificHelpers.WindowsLoaderMethods;
            var unixLoaderMethods = platformSpecificHelpers.UnixLoaderMethods;

            var isUnix = CreateIsUnixField(host, typeDef);

            var isUnixStaticFunction = CreateIsUnixStaticFunction(host, typeDef, getNewLine, stringOpEquality);
            var cctor = CreateCCtor(host, typeDef, isUnix, isUnixStaticFunction);

            var intPtrZero = new FieldReference
            {
                Name = host.NameTable.GetNameFor("Zero"),
                ContainingType = host.PlatformType.SystemIntPtr,
                Type = host.PlatformType.SystemIntPtr
            };

            var loadlibrary = CreateLoadLibrary(host, typeDef, windowsLoaderMethods.LoadLibrary, unixLoaderMethods.LoadLibrary, isUnix, intPtrZero);
            this.LoadLibrary = loadlibrary;

            var freelibrary = CreateFreeLibrary(host, typeDef, windowsLoaderMethods.FreeLibrary, unixLoaderMethods.FreeLibrary, isUnix);
            this.FreeLibrary = freelibrary;
            
            var getprocaddress = CreateGetProcAddress(host, typeDef, windowsLoaderMethods.GetProcAddress, unixLoaderMethods.GetProcAddress, isUnix, intPtrZero);
            this.GetProcAddress = getprocaddress;

            var getLength = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("get_Length"),
                ContainingType = host.PlatformType.SystemString,
                Type = host.PlatformType.SystemInt32,
                CallingConvention = CallingConvention.HasThis
            };

            var getChars = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("get_Chars"),
                ContainingType = host.PlatformType.SystemString,
                Type = host.PlatformType.SystemChar,
                CallingConvention = CallingConvention.HasThis,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemInt32 } }
            };

            var stringtoansi = CreateStringToAnsi(host, typeDef, getLength, getChars);
            this.StringToAnsiByteArray = stringtoansi;
            
            var stringToGlobalAnsi = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("StringToHGlobalAnsi"),
                ContainingType = this.SystemRuntimeInteropServicesMarshal,
                Type = host.PlatformType.SystemIntPtr,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Type = host.PlatformType.SystemString } }
            };

            var stringarraymarshallingprolog = CreateStringArrayMarshallingProlog(host, typeDef, stringToGlobalAnsi);
            this.StringArrayMarshallingProlog = stringarraymarshallingprolog;

            var intPtrOpInequality = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.OpInequality,
                ContainingType = host.PlatformType.SystemIntPtr,
                Type = host.PlatformType.SystemBoolean,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemIntPtr } }
            };

            var freeHGlobal = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("FreeHGlobal"),
                ContainingType = this.SystemRuntimeInteropServicesMarshal,
                Type = host.PlatformType.SystemVoid,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Type = host.PlatformType.SystemIntPtr } }
            };

            var stringarraymarshallingepilog = CreateStringArrayMarshallingEpilog(host, typeDef, intPtrZero, intPtrOpInequality, freeHGlobal);
            this.StringArrayMarshallingEpilog = stringarraymarshallingepilog;

            var isLibraryInitialized = CreateIsLibraryInitialized(host, typeDef, intPtrZero);
            this.IsLibraryInitialized = isLibraryInitialized;

            typeDef.Methods = new List<IMethodDefinition> { isUnixStaticFunction, cctor, loadlibrary, freelibrary, getprocaddress, stringtoansi, stringarraymarshallingprolog, stringarraymarshallingepilog, isLibraryInitialized };
            typeDef.Fields = new List<IFieldDefinition> { isUnix };

            typeDef.Methods.AddRange(platformSpecificHelpers.Methods);
        }

        public ITypeReference SystemRuntimeInteropServicesMarshal { get; }

        public ITypeReference SystemRuntimeCompilerServicesRuntimeHelpers { get; }

        public ITypeReference PInvokeHelpers { get; }
        
        public IMethodReference StringToAnsiByteArray { get; private set; }

        public IMethodReference StringArrayMarshallingProlog { get; private set; }

        public IMethodReference StringArrayMarshallingEpilog { get; private set; }

        public IMethodReference LoadLibrary { get; private set; }

        public IMethodReference GetProcAddress { get; private set; }

        private IMethodReference FreeLibrary { get; }

        public IMethodReference IsLibraryInitialized { get; private set; }
        
        private static IMethodDefinition CreateLoadLibrary(IMetadataHost host, ITypeDefinition typeDef, IMethodReference windowsLoadLibrary, IMethodReference unixLoadLibrary, IFieldReference isUnix, IFieldReference intPtrZero)
        {
            var methodDefinition = new MethodDefinition
            {
                Name = host.NameTable.GetNameFor("LoadLibrary"),
                ContainingTypeDefinition = typeDef,
                Parameters = new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString } },
                IsStatic = true,
                IsHiddenBySignature = true,
                Visibility = TypeMemberVisibility.Public,
                Type = host.PlatformType.SystemIntPtr
            };

            var ilGenerator = new ILGenerator(host, methodDefinition);

            var label = new ILGeneratorLabel();
            var retLabel = new ILGeneratorLabel();
            var dupLabel = new ILGeneratorLabel();

            var exceptionCtor = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor(".ctor"),
                ContainingType = host.PlatformType.SystemException,
                Type = host.PlatformType.SystemVoid,
                CallingConvention = CallingConvention.HasThis,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString } }
            };

            var stringConcat = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("Concat"),
                ContainingType = host.PlatformType.SystemString,
                Type = host.PlatformType.SystemString,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemString } }
            };

            var intPtrOpEquality = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.OpEquality,
                ContainingType = host.PlatformType.SystemIntPtr,
                Type = host.PlatformType.SystemBoolean,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemIntPtr } }
            };

            ilGenerator.Emit(OperationCode.Ldsfld, isUnix);
            ilGenerator.Emit(OperationCode.Brtrue_S, label);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Call, windowsLoadLibrary);
            ilGenerator.Emit(OperationCode.Br_S, dupLabel);
            ilGenerator.MarkLabel(label);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Call, unixLoadLibrary);
            ilGenerator.MarkLabel(dupLabel);
            ilGenerator.Emit(OperationCode.Dup);
            ilGenerator.Emit(OperationCode.Ldsfld, intPtrZero);
            ilGenerator.Emit(OperationCode.Call, intPtrOpEquality);
            ilGenerator.Emit(OperationCode.Brfalse_S, retLabel);
            ilGenerator.Emit(OperationCode.Ldstr, "LoadLibrary failed for: ");
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Call, stringConcat);
            ilGenerator.Emit(OperationCode.Newobj, exceptionCtor);
            ilGenerator.Emit(OperationCode.Throw);
            ilGenerator.MarkLabel(retLabel);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 8, methodDefinition, new List<ILocalDefinition>(), new List<ITypeDefinition>());

            return methodDefinition;
        }

        private static IMethodDefinition CreateGetProcAddress(IMetadataHost host, ITypeDefinition typeDef, IMethodReference windowsGetProcAddress, IMethodReference unixGetProcAddress, IFieldReference isUnix, IFieldReference intPtrZero)
        {
            var methodDefinition = new MethodDefinition
            {
                Name = host.NameTable.GetNameFor("GetProcAddress"),
                ContainingTypeDefinition = typeDef,
                Parameters = new List<IParameterDefinition>
                {
                    new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr },
                    new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemString }
                },
                IsStatic = true,
                IsHiddenBySignature = true,
                Visibility = TypeMemberVisibility.Public,
                Type = host.PlatformType.SystemIntPtr
            };

            var ilGenerator = new ILGenerator(host, methodDefinition);

            var label = new ILGeneratorLabel();
            var retLabel = new ILGeneratorLabel();
            var dupLabel = new ILGeneratorLabel();

            var exceptionCtor = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor(".ctor"),
                ContainingType = host.PlatformType.SystemException,
                Type = host.PlatformType.SystemVoid,
                CallingConvention = CallingConvention.HasThis,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString } }
            };

            var stringConcat = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("Concat"),
                ContainingType = host.PlatformType.SystemString,
                Type = host.PlatformType.SystemString,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemString } }
            };
            
            var intPtrOpEquality = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.OpEquality,
                ContainingType = host.PlatformType.SystemIntPtr,
                Type = host.PlatformType.SystemBoolean,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemIntPtr } }
            };

            ilGenerator.Emit(OperationCode.Ldsfld, isUnix);
            ilGenerator.Emit(OperationCode.Brtrue_S, label);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldarg_1);
            ilGenerator.Emit(OperationCode.Call, windowsGetProcAddress);
            ilGenerator.Emit(OperationCode.Br_S, dupLabel);
            ilGenerator.MarkLabel(label);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldarg_1);
            ilGenerator.Emit(OperationCode.Call, unixGetProcAddress);
            ilGenerator.MarkLabel(dupLabel);
            ilGenerator.Emit(OperationCode.Dup);
            ilGenerator.Emit(OperationCode.Ldsfld, intPtrZero);
            ilGenerator.Emit(OperationCode.Call, intPtrOpEquality);
            ilGenerator.Emit(OperationCode.Brfalse_S, retLabel);
            ilGenerator.Emit(OperationCode.Ldstr, "GetProcAddress failed for: ");
            ilGenerator.Emit(OperationCode.Ldarg_1);
            ilGenerator.Emit(OperationCode.Call, stringConcat);
            ilGenerator.Emit(OperationCode.Newobj, exceptionCtor);
            ilGenerator.Emit(OperationCode.Throw);
            ilGenerator.MarkLabel(retLabel);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 8, methodDefinition, new List<ILocalDefinition>(), new List<ITypeDefinition>());

            return methodDefinition;
        }

        private static IMethodDefinition CreateFreeLibrary(IMetadataHost host, ITypeDefinition typeDef, IMethodReference windowsFreeLibrary, IMethodReference unixFreeLibrary, IFieldReference isUnix)
        {
            var methodDefinition = new MethodDefinition
            {
                Name = host.NameTable.GetNameFor("FreeLibrary"),
                ContainingTypeDefinition = typeDef,
                Parameters = new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr } },
                IsStatic = true,
                IsHiddenBySignature = true,
                Visibility = TypeMemberVisibility.Public,
                Type = host.PlatformType.SystemInt32
            };

            var ilGenerator = new ILGenerator(host, methodDefinition);
            var label = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Ldsfld, isUnix);
            ilGenerator.Emit(OperationCode.Brtrue_S, label);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Call, windowsFreeLibrary);
            ilGenerator.Emit(OperationCode.Ret);
            ilGenerator.MarkLabel(label);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Call, unixFreeLibrary);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 8, methodDefinition, new List<ILocalDefinition>(), new List<ITypeDefinition>());

            return methodDefinition;
        }

        private static FieldDefinition CreateIsUnixField(IMetadataHost host, ITypeDefinition typeDef)
        {
            return new FieldDefinition
            {
                IsStatic = true,
                ContainingTypeDefinition = typeDef,
                IsReadOnly = true,
                Visibility = TypeMemberVisibility.Private,
                Type = host.PlatformType.SystemBoolean,
                Name = host.NameTable.GetNameFor("isUnix")
            };
        }

        private static IMethodDefinition CreateCCtor(IMetadataHost host, ITypeDefinition typeDef, IFieldReference fieldRef, IMethodReference isUnixStaticFunction)
        {
            var methodDefinition = new MethodDefinition
            {
                ContainingTypeDefinition = typeDef,
                IsStatic = true,
                IsSpecialName = true,
                IsRuntimeSpecial = true,
                Visibility = TypeMemberVisibility.Private,
                Type = host.PlatformType.SystemVoid,
                IsHiddenBySignature = true,
                Name = host.NameTable.GetNameFor(".cctor")
            };

            var ilGenerator = new ILGenerator(host, methodDefinition);
            ilGenerator.Emit(OperationCode.Call, isUnixStaticFunction);
            ilGenerator.Emit(OperationCode.Stsfld, fieldRef);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 8, methodDefinition, new List<ILocalDefinition>(), new List<ITypeDefinition>());

            return methodDefinition;
        }

        private static IMethodDefinition CreateIsUnixStaticFunction(IMetadataHost host, ITypeDefinition typeDef, IMethodReference getNewLine, IMethodReference stringOpEquality)
        {
            var methodDefinition = new MethodDefinition
            {
                ContainingTypeDefinition = typeDef,
                IsStatic = true,
                Visibility = TypeMemberVisibility.Assembly,
                Type = host.PlatformType.SystemBoolean,
                Name = host.NameTable.GetNameFor("IsUnix")
            };
            
            var ilGenerator = new ILGenerator(host, methodDefinition);
            ilGenerator.Emit(OperationCode.Call, getNewLine);
            ilGenerator.Emit(OperationCode.Ldstr, "\n");
            ilGenerator.Emit(OperationCode.Call, stringOpEquality);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 8, methodDefinition, new List<ILocalDefinition>(), new List<ITypeDefinition>());

            return methodDefinition;
        }

        private static NamespaceTypeDefinition CreatePInvokeHelpers(IRootUnitNamespace rootUnitNamespace, IMetadataHost host)
        {
            return new NamespaceTypeDefinition
            {
                ContainingUnitNamespace = rootUnitNamespace,
                IsPublic = false,
                IsAbstract = true,
                IsSealed = true,
                IsBeforeFieldInit = true,
                BaseClasses = new List<ITypeReference> { host.PlatformType.SystemObject },
                Name = host.NameTable.GetNameFor("PInvokeHelpers")
            };
        }

        private static IMethodDefinition CreateStringToAnsi(IMetadataHost host, ITypeDefinition typeDef, IMethodReference getLength, IMethodReference getChars)
        {
            var byteType = host.PlatformType.SystemUInt8;
            var byteArrayType = new VectorTypeReference { ElementType = byteType, Rank = 1 };

            MethodDefinition methodDefinition = new MethodDefinition
            {
                ContainingTypeDefinition = typeDef,
                IsStatic = true,
                Visibility = TypeMemberVisibility.Assembly,
                Type = byteArrayType.ResolvedArrayType,
                Parameters = new List<IParameterDefinition> { new ParameterDefinition { Type = host.PlatformType.SystemString } },
                Name = host.NameTable.GetNameFor("StringToAnsiByteArray")
            };
            
            var length = new LocalDefinition { Type = host.PlatformType.SystemInt32 };
            var byteArray = new LocalDefinition { Type = byteArrayType.ResolvedArrayType };
            var loopIndex = new LocalDefinition { Type = host.PlatformType.SystemInt32 };

            var locals = new List<ILocalDefinition> { length, byteArray, loopIndex };

            var ilGenerator = new ILGenerator(host, methodDefinition);
            var nullCaseLabel = new ILGeneratorLabel();
            var loopStart = new ILGeneratorLabel();
            var loopBackEdge = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Brtrue_S, nullCaseLabel);
            ilGenerator.Emit(OperationCode.Ldnull);
            ilGenerator.Emit(OperationCode.Ret);
            ilGenerator.MarkLabel(nullCaseLabel);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Call, getLength);
            ilGenerator.Emit(OperationCode.Stloc_0);
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Add);
            ilGenerator.Emit(OperationCode.Newarr, byteArrayType);
            ilGenerator.Emit(OperationCode.Stloc_1);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Stloc_2);
            ilGenerator.Emit(OperationCode.Br_S, loopStart);
            ilGenerator.MarkLabel(loopBackEdge);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldloc_2);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldloc_2);
            ilGenerator.Emit(OperationCode.Call, getChars);
            ilGenerator.Emit(OperationCode.Conv_U1);
            ilGenerator.Emit(OperationCode.Stelem_I1);
            ilGenerator.Emit(OperationCode.Ldloc_2);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Add);
            ilGenerator.Emit(OperationCode.Stloc_2);
            ilGenerator.MarkLabel(loopStart);
            ilGenerator.Emit(OperationCode.Ldloc_2);
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Blt_S, loopBackEdge);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Stelem_I1);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 8, methodDefinition, locals, new List<ITypeDefinition>());

            return methodDefinition;
        }

        private static IMethodDefinition CreateStringArrayMarshallingProlog(IMetadataHost host, ITypeDefinition typeDef, IMethodReference stringToHGlobalAnsi)
        {
            var stringArrayType = new VectorTypeReference { ElementType = host.PlatformType.SystemString, Rank = 1 };
            var intPtrArrayType = new VectorTypeReference { ElementType = host.PlatformType.SystemIntPtr, Rank = 1 };

            MethodDefinition methodDefinition = new MethodDefinition
            {
                ContainingTypeDefinition = typeDef,
                IsStatic = true,
                Visibility = TypeMemberVisibility.Assembly,
                Parameters = new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = stringArrayType }, new ParameterDefinition { Index = 1, Type = intPtrArrayType } },
                Type = host.PlatformType.SystemVoid,
                Name = host.NameTable.GetNameFor("StringArrayMarshallingProlog")
            };

            var size = new LocalDefinition { Type = host.PlatformType.SystemInt32 };
            var index = new LocalDefinition { Type = host.PlatformType.SystemInt32 };

            var locals = new List<ILocalDefinition> { size, index };

            var ilGenerator = new ILGenerator(host, methodDefinition);
            var loopStart = new ILGeneratorLabel();
            var loopBackEdge = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldlen);
            ilGenerator.Emit(OperationCode.Conv_I4);
            ilGenerator.Emit(OperationCode.Stloc_0);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Stloc_1);
            ilGenerator.Emit(OperationCode.Br_S, loopBackEdge);
            ilGenerator.MarkLabel(loopStart);
            ilGenerator.Emit(OperationCode.Ldarg_1);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldelem_Ref);
            ilGenerator.Emit(OperationCode.Call, stringToHGlobalAnsi);
            ilGenerator.Emit(OperationCode.Stelem_I);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Add);
            ilGenerator.Emit(OperationCode.Stloc_1);
            ilGenerator.MarkLabel(loopBackEdge);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Blt_S, loopStart);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 4, methodDefinition, locals, new List<ITypeDefinition>());

            return methodDefinition;
        }

        private static IMethodDefinition CreateStringArrayMarshallingEpilog(IMetadataHost host, ITypeDefinition typeDef, IFieldReference intPtrZero, IMethodReference intPtrOpInequality, IMethodReference freeHGlobal)
        {
            var intPtrArrayType = new VectorTypeReference { ElementType = host.PlatformType.SystemIntPtr, Rank = 1 };

            MethodDefinition methodDefinition = new MethodDefinition
            {
                ContainingTypeDefinition = typeDef,
                IsStatic = true,
                Visibility = TypeMemberVisibility.Assembly,
                Type = host.PlatformType.SystemVoid,
                Parameters = new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = intPtrArrayType } },
                Name = host.NameTable.GetNameFor("StringArrayMarshallingEpilog")
            };

            var size = new LocalDefinition { Type = host.PlatformType.SystemInt32 };
            var index = new LocalDefinition { Type = host.PlatformType.SystemInt32 };

            var locals = new List<ILocalDefinition> { size, index };

            var ilGenerator = new ILGenerator(host, methodDefinition);
            var loopStart = new ILGeneratorLabel();
            var loopBackEdge = new ILGeneratorLabel();
            var exitLabel = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldlen);
            ilGenerator.Emit(OperationCode.Conv_I4);
            ilGenerator.Emit(OperationCode.Stloc_0);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Stloc_1);
            ilGenerator.Emit(OperationCode.Br_S, loopBackEdge);
            ilGenerator.MarkLabel(loopStart);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldelem_I);
            ilGenerator.Emit(OperationCode.Ldsfld, intPtrZero);
            ilGenerator.Emit(OperationCode.Call, intPtrOpInequality);
            ilGenerator.Emit(OperationCode.Brfalse_S, exitLabel);
            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldelem_I);
            ilGenerator.Emit(OperationCode.Call, freeHGlobal);
            ilGenerator.MarkLabel(exitLabel);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Add);
            ilGenerator.Emit(OperationCode.Stloc_1);
            ilGenerator.MarkLabel(loopBackEdge);
            ilGenerator.Emit(OperationCode.Ldloc_1);
            ilGenerator.Emit(OperationCode.Ldloc_0);
            ilGenerator.Emit(OperationCode.Blt_S, loopStart);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 2, methodDefinition, locals, new List<ITypeDefinition>());

            return methodDefinition;
        }

        private static IMethodDefinition CreateIsLibraryInitialized(IMetadataHost host, ITypeDefinition typeDef, IFieldReference intPtrZero)
        {
            MethodDefinition methodDefinition = new MethodDefinition
            {
                ContainingTypeDefinition = typeDef,
                IsStatic = true,
                IsNeverInlined = true,
                IsHiddenBySignature = true,
                Visibility = TypeMemberVisibility.Assembly,
                Type = host.PlatformType.SystemVoid,
                Parameters = new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemString } },
                Name = host.NameTable.GetNameFor("IsLibraryInitialized")
            };
            
            var ilGenerator = new ILGenerator(host, methodDefinition);
            var retLabel = new ILGeneratorLabel();

            var exceptionCtor = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor(".ctor"),
                ContainingType = host.PlatformType.SystemException,
                Type = host.PlatformType.SystemVoid,
                CallingConvention = CallingConvention.HasThis,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString } }
            };

            var stringConcat = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("Concat"),
                ContainingType = host.PlatformType.SystemString,
                Type = host.PlatformType.SystemString,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemString }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemString }, new ParameterDefinition { Index = 2, Type = host.PlatformType.SystemString }, new ParameterDefinition { Index = 3, Type = host.PlatformType.SystemString } }
            };

            var intPtrOpEquality = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.OpEquality,
                ContainingType = host.PlatformType.SystemIntPtr,
                Type = host.PlatformType.SystemBoolean,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemIntPtr } }
            };

            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Ldsfld, intPtrZero);
            ilGenerator.Emit(OperationCode.Call, intPtrOpEquality);
            ilGenerator.Emit(OperationCode.Brfalse_S, retLabel);
            ilGenerator.Emit(OperationCode.Ldstr, "Library '");
            ilGenerator.Emit(OperationCode.Ldarg_1);
            ilGenerator.Emit(OperationCode.Ldstr, "' is not initialized. You must call LoadLibrary_");
            ilGenerator.Emit(OperationCode.Ldarg_1);
            ilGenerator.Emit(OperationCode.Call, stringConcat);
            ilGenerator.Emit(OperationCode.Newobj, exceptionCtor);
            ilGenerator.Emit(OperationCode.Throw);
            ilGenerator.MarkLabel(retLabel);
            ilGenerator.Emit(OperationCode.Ret);

            methodDefinition.Body = new ILGeneratorMethodBody(ilGenerator, true, 8, methodDefinition, new List<ILocalDefinition>(), new List<ITypeDefinition>());

            return methodDefinition;
        }

        private static INamespaceTypeReference CreateTypeReference(IMetadataHost host, IAssemblyReference assemblyReference, string typeName, bool isValueType = false, bool isEnum = false)
        {
            IUnitNamespaceReference ns = new Microsoft.Cci.Immutable.RootUnitNamespaceReference(assemblyReference);
            var names = typeName.Split('.');
            for (int i = 0, n = names.Length - 1; i < n; ++i)
            {
                ns = new Microsoft.Cci.Immutable.NestedUnitNamespaceReference(ns, host.NameTable.GetNameFor(names[i]));
            }

            return new Microsoft.Cci.Immutable.NamespaceTypeReference(host, ns, host.NameTable.GetNameFor(names[names.Length - 1]), 0, isEnum: isEnum, isValueType: isValueType);
        }
    }
}