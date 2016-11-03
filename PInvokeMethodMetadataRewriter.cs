namespace PInvokeCompiler
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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

        private readonly IMethodReference ptrToStringUnicode;

        private readonly IMethodReference getTypeFromHandle;

        private readonly IMethodReference getDelegateForFunctionPointer;

        private readonly IMethodReference stringArrayAnsiMarshallingProlog;

        private readonly IMethodReference stringArrayUnicodeMarshallingProlog;

        private readonly IMethodReference stringArrayMarshallingEpilog;

        private readonly ITypeReference skipTypeReference;

        public PInvokeMethodMetadataRewriter(InteropHelperReferences interopHelperReferences, IMetadataHost host, IMethodTransformationMetadataProvider metadataProvider)
            : base(host)
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

            this.ptrToStringUnicode = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = host.NameTable.GetNameFor("PtrToStringUni"),
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

            this.stringArrayAnsiMarshallingProlog = interopHelperReferences.StringArrayAnsiMarshallingProlog;
            this.stringArrayUnicodeMarshallingProlog = interopHelperReferences.StringArrayUnicodeMarshallingProlog;
            this.stringArrayMarshallingEpilog = interopHelperReferences.StringArrayMarshallingEpilog;
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

        private static void EmitBoolMarshalling(ILGenerator ilGenerator)
        {
            var trueCase = new ILGeneratorLabel();
            var falseCase = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Brtrue_S, trueCase);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Br_S, falseCase);
            ilGenerator.MarkLabel(trueCase);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.MarkLabel(falseCase);
        }

        private static void EmitBooleanReturnMarshalling(ILGenerator ilGenerator)
        {
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Cgt_Un);
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
            EmitBlittableTypeArrayMarshalling(locals, ilGenerator, byteArrayType.ResolvedArrayType);
        }

        private static void EmitStringReturnMarshalling(ILGenerator ilGenerator, IMethodReference ptrToStringAnsi, IMethodReference ptrToStringUnicode, StringFormatKind charSet, IMarshallingInformation returnTypeInfo)
        {
            bool doUnicodeMarshalling = false;
            if (returnTypeInfo != null && returnTypeInfo.UnmanagedType == UnmanagedType.LPWStr)
            {
                doUnicodeMarshalling = true;
            }
            else if (charSet == StringFormatKind.Unicode)
            {
                if (returnTypeInfo == null || (returnTypeInfo.UnmanagedType != UnmanagedType.LPStr &&
                    returnTypeInfo.UnmanagedType != UnmanagedType.LPTStr))
                {
                    doUnicodeMarshalling = true;
                }
            }

            ilGenerator.Emit(OperationCode.Call, doUnicodeMarshalling ? ptrToStringUnicode : ptrToStringAnsi);
        }
        
        private static void EmitBlittableTypeArrayMarshalling(List<ILocalDefinition> locals, ILGenerator ilGenerator, IArrayType arrayType)
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
            var paramToLocalMap = new Dictionary<IParameterDefinition, ILocalDefinition>();

            ilGenerator.Emit(OperationCode.Ldsfld, fieldDef);
            ilGenerator.Emit(OperationCode.Ldsfld, this.intPtrZero);
            ilGenerator.Emit(OperationCode.Call, this.intPtrOpEquality);
            ilGenerator.Emit(OperationCode.Brfalse_S, label);
            ilGenerator.Emit(OperationCode.Call, transformationMetadata.InitializeMethod);
            ilGenerator.MarkLabel(label);
            this.LoadArguments(locals, paramToLocalMap, ilGenerator, methodDefinition.ParameterCount, i => methodDefinition.Parameters[i], methodDefinition.PlatformInvokeData);
            ilGenerator.Emit(OperationCode.Ldsfld, fieldDef);
            ilGenerator.Emit(OperationCode.Call, transformationMetadata.NativeMethod);
            this.ReturnMarshalling(ilGenerator, methodDefinition);
            this.PostprocessNonBlittableArrayArguments(methodDefinition, locals, paramToLocalMap, ilGenerator);
            ilGenerator.Emit(OperationCode.Ret);

            var ilMethodBody = new ILGeneratorMethodBody(ilGenerator, true, (ushort)((methodDefinition.ParameterCount + 1) * 2), methodDefinition, locals, new List<ITypeDefinition>());
            methodDefinition.PlatformInvokeData = null;
            methodDefinition.Body = ilMethodBody;
        }

        private void ReturnMarshalling(ILGenerator ilGenerator, IMethodDefinition methodDefinition)
        {
            var returnType = methodDefinition.Type;

            if (TypeHelper.TypesAreEquivalent(returnType, host.PlatformType.SystemString))
            {
                EmitStringReturnMarshalling(ilGenerator, this.ptrToStringAnsi, this.ptrToStringUnicode, methodDefinition.PlatformInvokeData.StringFormat, methodDefinition.ReturnValueMarshallingInformation);
            }
            else if (returnType.ResolvedType.IsDelegate)
            {
                ilGenerator.Emit(OperationCode.Ldtoken, returnType);
                ilGenerator.Emit(OperationCode.Call, this.getTypeFromHandle);
                ilGenerator.Emit(OperationCode.Call, this.getDelegateForFunctionPointer);
                ilGenerator.Emit(OperationCode.Castclass, returnType);
            }
            else if (returnType.TypeCode == PrimitiveTypeCode.Boolean)
            {
                EmitBooleanReturnMarshalling(ilGenerator);
            }
        }

        private void PostprocessNonBlittableArrayArguments(IMethodDefinition methodDefinition, List<ILocalDefinition> locals, Dictionary<IParameterDefinition, ILocalDefinition> paramToLocalMap, ILGenerator ilGenerator)
        {
            bool hasReturnValue = methodDefinition.Type != this.host.PlatformType.SystemVoid;

            if (IsAnyParameterNonBlittableArray(methodDefinition))
            {
                var retLocal = new LocalDefinition
                {
                    IsPinned = false,
                    Type = methodDefinition.Type
                };

                if (hasReturnValue)
                {
                    locals.Add(retLocal);
                    ilGenerator.Emit(OperationCode.Stloc, retLocal);
                }

                var exitLabel = new ILGeneratorLabel();
                ilGenerator.Emit(OperationCode.Leave, exitLabel);
                ilGenerator.BeginFinallyBlock();

                foreach (var elem in methodDefinition.Parameters)
                {
                    ILocalDefinition t;
                    if (paramToLocalMap.TryGetValue(elem, out t))
                    {
                        ilGenerator.Emit(OperationCode.Ldloc, t);
                        ilGenerator.Emit(OperationCode.Call, this.stringArrayMarshallingEpilog); // TODO: Generalize for other array types
                    }
                }

                ilGenerator.Emit(OperationCode.Endfinally);
                ilGenerator.EndTryBody();
                ilGenerator.MarkLabel(exitLabel);

                if (hasReturnValue)
                {
                    ilGenerator.Emit(OperationCode.Ldloc, retLocal);
                }
            }
        }

        private void PreprocessNonBlittableArrayArguments(List<ILocalDefinition> locals, Dictionary<IParameterDefinition, ILocalDefinition> paramToLocalMap, ILGenerator ilGenerator, int argumentCount, Func<int, IParameterDefinition> parameterProvider)
        {
            bool beginTryBody = false;
            for (int i = 0; i < argumentCount; ++i)
            {
                var parameter = parameterProvider(i);
                var arrayType = parameter.Type.ResolvedType as IArrayType;
                if (arrayType != null)
                {
                    if (TypeHelper.TypesAreEquivalent(arrayType.ElementType, this.host.PlatformType.SystemString))
                    {
                        var intPtrArrayType = new VectorTypeReference { ElementType = this.host.PlatformType.SystemIntPtr, Rank = 1 }.ResolvedArrayType;

                        Ldarg(ilGenerator, parameter, i);
                        ilGenerator.Emit(OperationCode.Ldlen);
                        ilGenerator.Emit(OperationCode.Conv_I4); // I guess this breaks in large gc array mode
                        ilGenerator.Emit(OperationCode.Newarr, intPtrArrayType);

                        // IntPtr[]
                        var intPtrArray = new LocalDefinition
                        {
                            IsPinned = false,
                            Type = intPtrArrayType
                        };

                        locals.Add(intPtrArray);
                        paramToLocalMap.Add(parameter, intPtrArray);

                        ilGenerator.Emit(OperationCode.Stloc, intPtrArray);
                        beginTryBody = true;
                    }
                }
            }

            if (beginTryBody)
            {
                ilGenerator.BeginTryBody();
            }
        }

        private void EmitNonBlittableArrayMarshalling(List<ILocalDefinition> locals, ILGenerator ilGenerator, IArrayType arrayType, IParameterDefinition parameter, Dictionary<IParameterDefinition, ILocalDefinition> paramToLocalMap, IPlatformInvokeInformation pinvokeInfo)
        {
            if (TypeHelper.TypesAreEquivalent(arrayType.ElementType, this.host.PlatformType.SystemString))
            {
                var prologMethod = this.stringArrayAnsiMarshallingProlog;

                if (parameter.MarshallingInformation.ElementType == UnmanagedType.LPWStr)
                {
                    prologMethod = this.stringArrayUnicodeMarshallingProlog;
                }
                else if (pinvokeInfo.StringFormat == StringFormatKind.Unicode)
                {
                    if (parameter.MarshallingInformation.ElementType != UnmanagedType.LPStr && parameter.MarshallingInformation.ElementType != UnmanagedType.LPTStr)
                    {
                        prologMethod = this.stringArrayUnicodeMarshallingProlog;
                    }
                }

                var intPtrLocal = paramToLocalMap[parameter];
                ilGenerator.Emit(OperationCode.Ldloc, intPtrLocal);
                ilGenerator.Emit(OperationCode.Call, prologMethod);
                ilGenerator.Emit(OperationCode.Ldloc, intPtrLocal);
                EmitBlittableTypeArrayMarshalling(locals, ilGenerator, new VectorTypeReference { ElementType = this.host.PlatformType.SystemIntPtr, Rank = 1 }.ResolvedArrayType);
            }
            else
            {
                throw new Exception("NYI");
            }
        }

        private void LoadArguments(List<ILocalDefinition> locals, Dictionary<IParameterDefinition, ILocalDefinition> paramToLocalMap, ILGenerator ilGenerator, int argumentCount, Func<int, IParameterDefinition> parameterProvider, IPlatformInvokeInformation pinvokeInfo)
        {
            this.PreprocessNonBlittableArrayArguments(locals, paramToLocalMap, ilGenerator, argumentCount, parameterProvider);
            for (int i = 0; i < argumentCount; ++i)
            {
                var parameter = parameterProvider(i);
                Ldarg(ilGenerator, parameter, i);
                
                if (parameter.Type.ResolvedType is IArrayType)
                {
                    var arrayType = (IArrayType)parameter.Type.ResolvedType;

                    if (arrayType.IsBlittable())
                    {
                        EmitBlittableTypeArrayMarshalling(locals, ilGenerator, arrayType);
                    }
                    else
                    {
                        EmitNonBlittableArrayMarshalling(locals, ilGenerator, arrayType, parameter, paramToLocalMap, pinvokeInfo);
                    }
                }
                else if (parameter.IsByReference)
                {
                    EmitByRefMarshalling(locals, ilGenerator, parameter.Type);
                }
                else if (parameter.Type.TypeCode == PrimitiveTypeCode.Boolean)
                {
                    EmitBoolMarshalling(ilGenerator);
                }
                else if (parameter.Type.ResolvedType.IsDelegate)
                {
                    ilGenerator.Emit(OperationCode.Call, this.getFunctionPointerForDelegate);
                }
                else if (TypeHelper.TypesAreEquivalent(parameter.Type, this.host.PlatformType.SystemString))
                {
                    bool doUnicodeMarshalling = false;
                    if (parameter.MarshallingInformation.UnmanagedType == UnmanagedType.LPWStr)
                    {
                        doUnicodeMarshalling = true;
                    }
                    else if (pinvokeInfo.StringFormat == StringFormatKind.Unicode)
                    {
                        if (parameter.MarshallingInformation.UnmanagedType != UnmanagedType.LPStr &&
                            parameter.MarshallingInformation.UnmanagedType != UnmanagedType.LPTStr)
                        {
                            doUnicodeMarshalling = true;
                        }
                    }

                    if (doUnicodeMarshalling)
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

        private static void Ldarg(ILGenerator ilGenerator, IParameterDefinition parameter, int i)
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
                    ilGenerator.Emit(i <= byte.MaxValue ? OperationCode.Ldarg_S : OperationCode.Ldarg, parameter);
                    break;
            }
        }

        private static bool IsAnyParameterNonBlittableArray(IMethodDefinition methodDefinition)
        {
            return methodDefinition.Parameters.Any(parameter => IsNonBlittableArray(parameter.Type));
        }

        private static bool IsNonBlittableArray(ITypeReference typeRef)
        {
            var arrayType = typeRef.ResolvedType as IArrayType;
            if (arrayType != null)
            {
                if (!arrayType.IsBlittable())
                {
                    return true;
                }
            }

            return false;
        }
    }
}