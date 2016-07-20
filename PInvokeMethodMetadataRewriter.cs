namespace PInvokeCompiler
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using Microsoft.Cci;
    using Microsoft.Cci.MutableCodeModel;

    internal sealed class PInvokeMethodMetadataRewriter : MetadataRewriter
    {
        private readonly IMethodTransformationMetadataProvider metadataProvider;

        private readonly FieldReference intPtrZero;

        private readonly IMethodReference intPtrOpEquality;

        private readonly IMethodReference getFunctionPointerForDelegate;

        private readonly IMethodReference getOffSetToStringData;

        private readonly IMethodReference stringToAnsiArray;

        private readonly IMethodReference ptrToStringAnsi;

        private readonly IMethodReference getTypeFromHandle;

        private readonly IMethodReference getDelegateForFunctionPointer;

        private readonly ITypeReference skipTypeReference;

        public PInvokeMethodMetadataRewriter(InteropHelperReferences interopHelperReferences, IMetadataHost host, IMethodTransformationMetadataProvider metadataProvider)
            : base(host, copyAndRewriteImmutableReferences: false)
        {
            this.metadataProvider = metadataProvider;
            var platformType = host.PlatformType;
            var nameTable = host.NameTable;

            this.intPtrZero = new FieldReference
            {
                Name = nameTable.GetNameFor("Zero"),
                ContainingType = platformType.SystemIntPtr,
                Type = platformType.SystemIntPtr
            };

            this.stringToAnsiArray = interopHelperReferences.StringToAnsiByteArray;

            this.getOffSetToStringData = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = nameTable.GetNameFor("get_OffsetToStringData"),
                ContainingType = interopHelperReferences.SystemRuntimeCompilerServicesRuntimeHelpers,
                Type = platformType.SystemInt32
            };

            this.intPtrOpEquality = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = nameTable.OpEquality,
                ContainingType = platformType.SystemIntPtr,
                Type = platformType.SystemBoolean,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = platformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = platformType.SystemIntPtr } }
            };

            this.getFunctionPointerForDelegate = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = nameTable.GetNameFor("GetFunctionPointerForDelegate"),
                ContainingType = interopHelperReferences.SystemRuntimeInteropServicesMarshal,
                Type = platformType.SystemIntPtr,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = platformType.SystemDelegate } }
            };

            this.getFunctionPointerForDelegate = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = nameTable.GetNameFor("GetFunctionPointerForDelegate"),
                ContainingType = interopHelperReferences.SystemRuntimeInteropServicesMarshal,
                Type = platformType.SystemIntPtr,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = platformType.SystemDelegate } }
            };

            this.ptrToStringAnsi = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("PtrToStringAnsi"),
                ContainingType = interopHelperReferences.SystemRuntimeInteropServicesMarshal,
                Type = host.PlatformType.SystemString,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr } }
            };

            this.getTypeFromHandle = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("GetTypeFromHandle"),
                ContainingType = host.PlatformType.SystemType,
                Type = host.PlatformType.SystemType,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemRuntimeTypeHandle } }
            };

            this.getDelegateForFunctionPointer = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("GetDelegateForFunctionPointer"),
                ContainingType = interopHelperReferences.SystemRuntimeInteropServicesMarshal,
                Type = host.PlatformType.SystemDelegate,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = host.PlatformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = host.PlatformType.SystemType } }
            };

            this.skipTypeReference = interopHelperReferences.PInvokeHelpers;
        }

        public override void RewriteChildren(MethodDefinition method)
        {
            if (method.IsPlatformInvoke)
            {
                if (!TypeHelper.TypesAreEquivalent(method.ContainingTypeDefinition, this.skipTypeReference))
                {
                    this.TransformPInvokeMethodDefinitionToImplementedMethodDefinition(method);
                }
            }

            base.RewriteChildren(method);
        }

        private static void EmitUnicodeStringMarshalling(List<ILocalDefinition> locals, ILGenerator ilGenerator, IMethodReference getOffsetToStringData, ITypeReference stringType)
        {
            var pinnedLocal = new LocalDefinition { IsPinned = true, Type = stringType };
            locals.Add(pinnedLocal);

            var nullCaseLabel = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Stloc, pinnedLocal);
            ilGenerator.Emit(OperationCode.Ldloc, pinnedLocal);
            ilGenerator.Emit(OperationCode.Conv_I);
            ilGenerator.Emit(OperationCode.Dup);
            ilGenerator.Emit(OperationCode.Brfalse_S, nullCaseLabel);
            ilGenerator.Emit(OperationCode.Call, getOffsetToStringData);
            ilGenerator.Emit(OperationCode.Add);
            ilGenerator.MarkLabel(nullCaseLabel);
        }

        private static void EmitAnsiStringMarshalling(List<ILocalDefinition> locals, ILGenerator ilGenerator, IMethodReference stringToAnsiByteArray, IPlatformType platformType)
        {
            var byteType = platformType.SystemUInt8;
            var byteArrayType = new VectorTypeReference { ElementType = byteType, Rank = 1 };
            
            ilGenerator.Emit(OperationCode.Call, stringToAnsiByteArray);
            EmitArrayMarshalling(locals, ilGenerator, byteArrayType.ResolvedArrayType);
        }

        private static void EmitStringReturnMarshalling(ILGenerator ilGenerator, IMethodReference ptrToStringAnsi)
        {
            ilGenerator.Emit(OperationCode.Call, ptrToStringAnsi); // TODO: support Unicode
        }

        private static void EmitArrayMarshalling(List<ILocalDefinition> locals, ILGenerator ilGenerator, IArrayType arrayType)
        {
            var nullCaseLabel = new ILGeneratorLabel();

            // [0] T& pinned x
            var pinnedLocal = new LocalDefinition
            {
                IsPinned = true,
                IsReference = true,
                Type = arrayType.ElementType
            };

            // [1] T[] V_1,
            var duplicatearray = new LocalDefinition
            {
                IsPinned = false,
                Type = arrayType
            };

            locals.Add(pinnedLocal);
            locals.Add(duplicatearray);

            ilGenerator.Emit(OperationCode.Dup);
            ilGenerator.Emit(OperationCode.Stloc, duplicatearray);
            ilGenerator.Emit(OperationCode.Brfalse, nullCaseLabel);
            ilGenerator.Emit(OperationCode.Ldloc, duplicatearray);
            ilGenerator.Emit(OperationCode.Ldlen);
            ilGenerator.Emit(OperationCode.Conv_I4);
            ilGenerator.Emit(OperationCode.Brfalse, nullCaseLabel);
            ilGenerator.Emit(OperationCode.Ldloc, duplicatearray);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Ldelema, arrayType.ElementType);
            ilGenerator.Emit(OperationCode.Stloc, pinnedLocal);
            ilGenerator.MarkLabel(nullCaseLabel);
            ilGenerator.Emit(OperationCode.Ldloc, pinnedLocal);
            ilGenerator.Emit(OperationCode.Conv_I);
        }

        private static void EmitByRefMarshalling(List<ILocalDefinition> locals, ILGenerator ilGenerator, ITypeReference typeRef)
        {
            var pinnedLocal = new LocalDefinition
            {
                IsReference = true,
                IsPinned = true,
                Type = typeRef
            };

            locals.Add(pinnedLocal);

            ilGenerator.Emit(OperationCode.Stloc, pinnedLocal);
            ilGenerator.Emit(OperationCode.Ldloc, pinnedLocal);
            ilGenerator.Emit(OperationCode.Conv_I);
        }

        private void TransformPInvokeMethodDefinitionToImplementedMethodDefinition(MethodDefinition methodDefinition)
        {
            methodDefinition.IsPlatformInvoke = false;
            methodDefinition.IsExternal = false;
            methodDefinition.PreserveSignature = false;

            var ilGenerator = new ILGenerator(this.host, methodDefinition);
            var label = new ILGeneratorLabel();
            var transformationMetadata = this.metadataProvider.Retrieve(methodDefinition);

            var fieldDef = transformationMetadata.FunctionPointer;
            var locals = new List<ILocalDefinition>();

            ilGenerator.Emit(OperationCode.Ldsfld, fieldDef);
            ilGenerator.Emit(OperationCode.Ldsfld, this.intPtrZero);
            ilGenerator.Emit(OperationCode.Call, this.intPtrOpEquality);
            ilGenerator.Emit(OperationCode.Brfalse_S, label);
            ilGenerator.Emit(OperationCode.Call, transformationMetadata.InitializeMethod);
            ilGenerator.Emit(OperationCode.Stsfld, fieldDef);
            ilGenerator.MarkLabel(label);
            this.LoadArguments(locals, ilGenerator, methodDefinition.ParameterCount, i => methodDefinition.Parameters[i]);
            ilGenerator.Emit(OperationCode.Ldsfld, fieldDef);
            ilGenerator.Emit(OperationCode.Call, transformationMetadata.NativeMethod);
            this.ReturnMarshalling(ilGenerator, methodDefinition);
            ilGenerator.Emit(OperationCode.Ret);

            var ilMethodBody = new ILGeneratorMethodBody(ilGenerator, true, (ushort)((methodDefinition.ParameterCount + 1) * 2), methodDefinition, locals, new List<ITypeDefinition>());
            methodDefinition.Body = ilMethodBody;
        }

        private void ReturnMarshalling(ILGenerator ilGenerator, IMethodDefinition methodDefinition)
        {
            var returnType = methodDefinition.Type;

            if (TypeHelper.TypesAreEquivalent(returnType, host.PlatformType.SystemString))
            {
                EmitStringReturnMarshalling(ilGenerator, this.ptrToStringAnsi);
            }
            else if (returnType.ResolvedType.IsDelegate)
            {
                ilGenerator.Emit(OperationCode.Ldtoken, returnType);
                ilGenerator.Emit(OperationCode.Call, this.getTypeFromHandle);
                ilGenerator.Emit(OperationCode.Call, this.getDelegateForFunctionPointer);
                ilGenerator.Emit(OperationCode.Castclass, returnType);
            }
        }

        private void LoadArguments(List<ILocalDefinition> locals, ILGenerator ilGenerator, int argumentCount, Func<int, IParameterDefinition> parameterProvider)
        {
            for (int i = 0; i < argumentCount; ++i)
            {
                var parameter = parameterProvider(i);

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
                        ilGenerator.Emit(i <= byte.MaxValue ? OperationCode.Ldarg_S : OperationCode.Ldarg, parameter);
                        break;
                }

                if (parameter.Type.ResolvedType is IArrayType)
                {
                    EmitArrayMarshalling(locals, ilGenerator, (IArrayType)parameter.Type.ResolvedType);
                }
                else if (parameter.IsByReference)
                {
                    EmitByRefMarshalling(locals, ilGenerator, parameter.Type);
                }
                else if (parameter.Type.ResolvedType.IsDelegate)
                {
                    ilGenerator.Emit(OperationCode.Call, this.getFunctionPointerForDelegate);
                }
                else if (TypeHelper.TypesAreEquivalent(parameter.Type, this.host.PlatformType.SystemString))
                {
                    if (parameter.MarshallingInformation.UnmanagedType == UnmanagedType.LPWStr)
                    {
                        EmitUnicodeStringMarshalling(locals, ilGenerator, this.getOffSetToStringData, this.host.PlatformType.SystemString);
                    }
                    else
                    {
                        EmitAnsiStringMarshalling(locals, ilGenerator, this.stringToAnsiArray, this.host.PlatformType);
                    }
                }
            }
        }
    }
}