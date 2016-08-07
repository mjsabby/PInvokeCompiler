namespace PInvokeCompiler
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Cci;
    using Microsoft.Cci.MutableCodeModel;

    internal sealed class MethodTransformationMetadataRewriter : MetadataRewriter, IMethodTransformationMetadataProvider
    {
        private readonly IPInvokeMethodsProvider methodsProvider;

        private readonly IPlatformType platformType;

        private readonly INameTable nameTable;

        private readonly Dictionary<IMethodDefinition, IMethodTransformationMetadata> methodTransformationTable = new Dictionary<IMethodDefinition, IMethodTransformationMetadata>();

        private readonly IMethodReference loadLibrary;

        private readonly IMethodReference getProcAddress;

        public MethodTransformationMetadataRewriter(IMethodReference loadLibrary, IMethodReference getProcAddress, IMetadataHost host, IPInvokeMethodsProvider methodsProvider)
            : base(host, copyAndRewriteImmutableReferences: false)
        {
            this.platformType = host.PlatformType;
            this.nameTable = host.NameTable;
            this.methodsProvider = methodsProvider;
            this.loadLibrary = loadLibrary;
            this.getProcAddress = getProcAddress;
        }

        public override void RewriteChildren(NamedTypeDefinition typeDefinition)
        {
            var dict = new Dictionary<IModuleReference, IFieldDefinition>();
            var moduleRefs = this.methodsProvider.RetrieveModuleRefs(typeDefinition);

            if (typeDefinition.Fields == null)
            {
                typeDefinition.Fields = new List<IFieldDefinition>();
            }

            foreach (var moduleRef in moduleRefs)
            {
                var fieldDef = this.CreateFunctionPointerField(typeDefinition, "pl_" + moduleRef.Name.Value);
                var loadLibMethodDef = this.CreateLoadLibraryMethod(typeDefinition, moduleRef, fieldDef);

                typeDefinition.Fields.Add(fieldDef);
                typeDefinition.Methods.Add(loadLibMethodDef);
                dict.Add(moduleRef, fieldDef);
            }

            var methodDefinitions = this.methodsProvider.RetrieveMethodDefinitions(typeDefinition);
            foreach (var methodDefinition in methodDefinitions)
            {
                var fieldDef = this.CreateFunctionPointerField(typeDefinition, "p_" + methodDefinition.Name.Value);
                var initMethodDef = this.CreateInitMethod(methodDefinition, dict[methodDefinition.PlatformInvokeData.ImportModule], fieldDef, methodDefinition.PlatformInvokeData.ImportName.Value);
                var nativeMethodDef = this.CreateNativeMethod(methodDefinition);
                
                typeDefinition.Fields.Add(fieldDef);
                typeDefinition.Methods.Add(nativeMethodDef);
                typeDefinition.Methods.Add(initMethodDef);

                this.methodTransformationTable.Add(methodDefinition, new MethodTransformationMetadata(initMethodDef, fieldDef, nativeMethodDef));
            }

            base.RewriteChildren(typeDefinition);
        }

        public IMethodTransformationMetadata Retrieve(IMethodDefinition methodDefinition)
        {
            return this.methodTransformationTable[methodDefinition];
        }

        private static MethodDefinition CreateRegularMethodDefinitionFromPInvokeMethodDefinition(IMethodDefinition methodDefinition, ITypeReference intPtrType)
        {
            var returnType = !IsBlittableType(methodDefinition.Type) || methodDefinition.ReturnValueIsByRef ? intPtrType : methodDefinition.Type; // TODO: return byref handled properly?

            var nativeMethodDef = new MethodDefinition
            {
                Type = returnType,
                Name = methodDefinition.Name,
                ContainingTypeDefinition = methodDefinition.ContainingTypeDefinition,
                IsAggressivelyInlined = true,
                IsStatic = true,
                Visibility = TypeMemberVisibility.Private,
                AcceptsExtraArguments = methodDefinition.AcceptsExtraArguments,
                Parameters = new List<IParameterDefinition>()
            };

            ushort j = 0;
            foreach (var parameter in methodDefinition.Parameters)
            {
                var paramDef = new ParameterDefinition { Type = parameter.Type, Index = j++, Name = parameter.Name };
                if (!IsBlittableType(parameter.Type) || parameter.IsByReference)
                {
                    paramDef.Type = intPtrType;
                }

                nativeMethodDef.Parameters.Add(paramDef);
            }

            return nativeMethodDef;
        }

        private static void LoadArguments(ILGenerator ilGenerator, int argumentCount, Func<int, IParameterDefinition> parameterProvider)
        {
            for (int i = 0; i < argumentCount; ++i)
            {
                switch (i)
                {
                    case 0:
                        ilGenerator.Emit(OperationCode.Ldarg_0);
                        break;
                    case 1:
                        ilGenerator.Emit(OperationCode.Ldarg_1);
                        break;
                    case 2:
                        ilGenerator.Emit(OperationCode.Ldarg_2);
                        break;
                    case 3:
                        ilGenerator.Emit(OperationCode.Ldarg_3);
                        break;
                    default:
                        ilGenerator.Emit(i <= byte.MaxValue ? OperationCode.Ldarg_S : OperationCode.Ldarg, parameterProvider(i));
                        break;
                }
            }
        }

        private static bool IsBlittableType(ITypeReference typeRef)
        {
            var typeCode = typeRef.TypeCode;

            switch (typeCode)
            {
                case PrimitiveTypeCode.Void:
                case PrimitiveTypeCode.Int8:
                case PrimitiveTypeCode.UInt8:
                case PrimitiveTypeCode.Int16:
                case PrimitiveTypeCode.UInt16:
                case PrimitiveTypeCode.Int32:
                case PrimitiveTypeCode.UInt32:
                case PrimitiveTypeCode.Int64:
                case PrimitiveTypeCode.UInt64:
                case PrimitiveTypeCode.IntPtr:
                case PrimitiveTypeCode.UIntPtr:
                case PrimitiveTypeCode.Float32:
                case PrimitiveTypeCode.Float64:
                case PrimitiveTypeCode.Pointer:
                    return true;
                case PrimitiveTypeCode.Char:
                case PrimitiveTypeCode.Boolean:
                    return false;
            }
            
            if (typeRef.IsValueType)
            {
                var typeDef = typeRef.ResolvedType;

                if (string.Equals(typeDef.ToString(), "Microsoft.Cci.DummyNamespaceTypeDefinition"))
                {
                    throw new Exception($"Unable to find type def for {typeRef}. The assembly this type is defined in was not loaded");
                }

                foreach (var fieldInfo in typeDef.Fields)
                {
                    if (fieldInfo.IsStatic)
                    {
                        continue;
                    }

                    if (fieldInfo.IsMarshalledExplicitly || !IsBlittableType(fieldInfo.Type))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        private IFieldDefinition CreateFunctionPointerField(INamedTypeDefinition typeDefinition, string fieldName)
        {
            return new FieldDefinition
            {
                IsStatic = true,
                Visibility = TypeMemberVisibility.Private,
                Type = this.platformType.SystemIntPtr,
                ContainingTypeDefinition = typeDefinition,
                Name = this.nameTable.GetNameFor(fieldName)
            };
        }

        private IMethodDefinition CreateLoadLibraryMethod(ITypeDefinition typeDefinition, IModuleReference moduleRef, IFieldReference fieldRef)
        {
            var methodDefinition = new MethodDefinition
            {
                IsStatic = true,
                Type = this.platformType.SystemVoid,
                ContainingTypeDefinition = typeDefinition,
                Name = this.nameTable.GetNameFor("LoadLibrary" + moduleRef.Name.Value),
                IsNeverInlined = true,
                Visibility = TypeMemberVisibility.Public,
                Parameters = new List<IParameterDefinition> { new ParameterDefinition { Index = 0, Type = this.platformType.SystemString } }
            };

            var ilGenerator = new ILGenerator(this.host, methodDefinition);

            ilGenerator.Emit(OperationCode.Ldarg_0);
            ilGenerator.Emit(OperationCode.Call, this.loadLibrary);
            ilGenerator.Emit(OperationCode.Stsfld, fieldRef);
            ilGenerator.Emit(OperationCode.Ret);

            var ilMethodBody = new ILGeneratorMethodBody(ilGenerator, false, 2, methodDefinition, Enumerable.Empty<ILocalDefinition>(), Enumerable.Empty<ITypeDefinition>());
            methodDefinition.Body = ilMethodBody;

            return methodDefinition;
        }

        private IMethodDefinition CreateInitMethod(IMethodDefinition incomingMethodDefinition, IFieldReference loadLibraryModule, IFieldReference methodField, string entryPoint)
        {
            var methodDefinition = new MethodDefinition
            {
                IsStatic = true,
                Type = this.platformType.SystemVoid,
                ContainingTypeDefinition = incomingMethodDefinition.ContainingTypeDefinition,
                Name = this.nameTable.GetNameFor("init_" + incomingMethodDefinition.Name.Value),
                IsNeverInlined = true,
                Visibility = TypeMemberVisibility.Private
            };

            var ilGenerator = new ILGenerator(this.host, methodDefinition);

            ilGenerator.Emit(OperationCode.Ldsfld, loadLibraryModule);
            ilGenerator.Emit(OperationCode.Ldstr, entryPoint);
            ilGenerator.Emit(OperationCode.Call, this.getProcAddress);
            ilGenerator.Emit(OperationCode.Stsfld, methodField);
            ilGenerator.Emit(OperationCode.Ret);

            var ilMethodBody = new ILGeneratorMethodBody(ilGenerator, false, 2, methodDefinition, Enumerable.Empty<ILocalDefinition>(), Enumerable.Empty<ITypeDefinition>());
            methodDefinition.Body = ilMethodBody;

            return methodDefinition;
        }

        private IMethodDefinition CreateNativeMethod(IMethodDefinition pinvokeMethodDefinition)
        {
            var pinvokeData = pinvokeMethodDefinition.PlatformInvokeData;

            var nativeMethodDef = CreateRegularMethodDefinitionFromPInvokeMethodDefinition(pinvokeMethodDefinition, this.host.PlatformType.SystemIntPtr);
            nativeMethodDef.Parameters.Add(new ParameterDefinition { Type = this.platformType.SystemIntPtr, Index = pinvokeMethodDefinition.ParameterCount, Name = this.nameTable.GetNameFor("funcPtr") });

            var ilGenerator = new ILGenerator(this.host, nativeMethodDef);
            LoadArguments(ilGenerator, pinvokeMethodDefinition.ParameterCount + 1, i => nativeMethodDef.Parameters[i]);

            var returnType = !IsBlittableType(pinvokeMethodDefinition.Type) ? this.platformType.SystemIntPtr : pinvokeMethodDefinition.Type;

            var funcPtr = new FunctionPointerTypeReference
            {
                Type = returnType,
                Parameters = new List<IParameterTypeInformation>()
            };

            switch (pinvokeData.PInvokeCallingConvention)
            {
                case PInvokeCallingConvention.CDecl:
                    funcPtr.CallingConvention = CallingConvention.C;
                    break;
                case PInvokeCallingConvention.FastCall:
                    funcPtr.CallingConvention = CallingConvention.FastCall;
                    break;
                case PInvokeCallingConvention.StdCall:
                case PInvokeCallingConvention.WinApi:
                    funcPtr.CallingConvention = CallingConvention.Standard;
                    break;
                case PInvokeCallingConvention.ThisCall:
                    funcPtr.CallingConvention = CallingConvention.ThisCall;
                    break;
            }

            foreach (var parameter in pinvokeMethodDefinition.Parameters)
            {
                var paramDef = new ParameterDefinition { Type = parameter.Type };

                if (!IsBlittableType(parameter.Type) || parameter.IsByReference)
                {
                    paramDef.Type = this.platformType.SystemIntPtr;
                }

                funcPtr.Parameters.Add(paramDef);
            }

            ilGenerator.Emit(OperationCode.Calli, (ISignature)funcPtr);
            ilGenerator.Emit(OperationCode.Ret);

            var ilMethodBody = new ILGeneratorMethodBody(ilGenerator, false, nativeMethodDef.ParameterCount, nativeMethodDef, Enumerable.Empty<ILocalDefinition>(), Enumerable.Empty<ITypeDefinition>());
            nativeMethodDef.Body = ilMethodBody;

            return nativeMethodDef;
        }
    }
}