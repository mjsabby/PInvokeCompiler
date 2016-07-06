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

        private readonly Microsoft.Cci.MutableCodeModel.MethodReference intPtrOpEquality;

        private readonly Microsoft.Cci.MutableCodeModel.MethodReference intPtrSize;

        private readonly Microsoft.Cci.MutableCodeModel.MethodReference getFunctionPointerForDelegate;

        private readonly Microsoft.Cci.MutableCodeModel.MethodReference getLength;

        private readonly Microsoft.Cci.MutableCodeModel.MethodReference getChars;

        public PInvokeMethodMetadataRewriter(IEnumerable<IAssemblyReference> assemblyReferences, IMetadataHost host, IPlatformType platformType, INameTable nameTable, IMethodTransformationMetadataProvider metadataProvider)
            : base(host, copyAndRewriteImmutableReferences: false)
        {
            this.metadataProvider = metadataProvider;

            this.intPtrZero = new FieldReference
            {
                Name = nameTable.GetNameFor("Zero"),
                ContainingType = platformType.SystemIntPtr,
                Type = platformType.SystemIntPtr
            };

            this.getLength = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = nameTable.GetNameFor("get_Length"),
                ContainingType = platformType.SystemString,
                Type = platformType.SystemInt32,
                CallingConvention = Microsoft.Cci.CallingConvention.HasThis
            };

            this.getChars = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = nameTable.GetNameFor("get_Chars"),
                ContainingType = platformType.SystemString,
                Type = platformType.SystemChar,
                CallingConvention = Microsoft.Cci.CallingConvention.HasThis,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = platformType.SystemInt32 } }
            };

            this.intPtrSize = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = nameTable.GetNameFor("get_Size"),
                ContainingType = platformType.SystemIntPtr,
                Type = platformType.SystemInt32
            };

            this.intPtrOpEquality = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = nameTable.GetNameFor("op_Equality"),
                ContainingType = platformType.SystemIntPtr,
                Type = platformType.SystemBoolean,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = platformType.SystemIntPtr }, new ParameterDefinition { Index = 1, Type = platformType.SystemIntPtr } }
            };

            IAssemblyReference first = null;
            foreach (var t in assemblyReferences)
            {
                if (t.Name.Value.Equals("System.Runtime.InteropServices"))
                {
                    first = t;
                    break;
                }
            }

            var interopServicesAssembly = first ?? assemblyReferences.First(t => t.Name.Value.Equals("mscorlib"));

            this.getFunctionPointerForDelegate = new Microsoft.Cci.MutableCodeModel.MethodReference
            {
                Name = nameTable.GetNameFor("GetFunctionPointerForDelegate"),
                ContainingType = CreateTypeReference(host, interopServicesAssembly, "System.Runtime.InteropServices.Marshal"),
                Type = platformType.SystemIntPtr,
                Parameters = new List<IParameterTypeInformation> { new ParameterDefinition { Index = 0, Type = platformType.SystemDelegate } }
            };
        }

        public override void RewriteChildren(MethodDefinition method)
        {
            if (method.IsPlatformInvoke)
            {
                this.TransformPInvokeMethodDefinitionToImplementedMethodDefinition(method);
            }

            base.RewriteChildren(method);
        }

        private static void EmitUnicodeStringMarshalling(List<ILocalDefinition> locals, ILGenerator ilGenerator, IMethodReference intPtrSize, ITypeReference stringType)
        {
            var pinnedLocal = new LocalDefinition { IsPinned = true, IsReference = true, Type = stringType };
            locals.Add(pinnedLocal);

            var nullCaseLabel = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Stloc, pinnedLocal);
            ilGenerator.Emit(OperationCode.Ldloc, pinnedLocal);
            ilGenerator.Emit(OperationCode.Conv_I);
            ilGenerator.Emit(OperationCode.Dup);
            ilGenerator.Emit(OperationCode.Brfalse_S, nullCaseLabel);
            ilGenerator.Emit(OperationCode.Call, intPtrSize);
            ilGenerator.Emit(OperationCode.Ldc_I4_4);
            ilGenerator.Emit(OperationCode.Add);
            ilGenerator.Emit(OperationCode.Add);
            ilGenerator.MarkLabel(nullCaseLabel);
        }

        private static void EmitAnsiStringMarshalling(List<ILocalDefinition> locals, ILGenerator ilGenerator, IParameterDefinition parameter, IMethodReference getLength, IMethodReference getChars, IPlatformType platformType)
        {
            var byteType = platformType.SystemUInt8;
            var byteArrayType = new VectorTypeReference { ElementType = byteType, Rank = 1 };

            var byteArray = new LocalDefinition { Type = byteArrayType.ResolvedArrayType };
            var loopIndex = new LocalDefinition { Type = platformType.SystemInt32 };
            var pinnedLocal = new LocalDefinition { IsPinned = true, IsReference = true, Type = byteType };

            var local = new LocalDefinition { Type = platformType.SystemString };

            locals.Add(byteArray);
            locals.Add(loopIndex);
            locals.Add(pinnedLocal);
            locals.Add(local);

            var loopBackEdge = new ILGeneratorLabel();
            var loopStart = new ILGeneratorLabel();
            var nullLabelCase = new ILGeneratorLabel();
            var methodExitLabel = new ILGeneratorLabel();

            ilGenerator.Emit(OperationCode.Stloc, local);
            ilGenerator.Emit(OperationCode.Pop);
            ilGenerator.Emit(OperationCode.Pop);
            ilGenerator.Emit(OperationCode.Ldloc, local);
            ilGenerator.Emit(OperationCode.Brfalse, nullLabelCase);
            ilGenerator.Emit(OperationCode.Ldloc, local);
            ilGenerator.Emit(OperationCode.Call, getLength);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Add);
            ilGenerator.Emit(OperationCode.Newarr, byteArrayType);
            ilGenerator.Emit(OperationCode.Stloc, byteArray);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Stloc, loopIndex);
            ilGenerator.Emit(OperationCode.Br_S, loopStart);
            ilGenerator.MarkLabel(loopBackEdge);
            ilGenerator.Emit(OperationCode.Ldloc, byteArray);
            ilGenerator.Emit(OperationCode.Ldloc, loopIndex);
            ilGenerator.Emit(OperationCode.Ldarg, parameter);
            ilGenerator.Emit(OperationCode.Ldloc, loopIndex);
            ilGenerator.Emit(OperationCode.Call, getChars);
            ilGenerator.Emit(OperationCode.Conv_U1);
            ilGenerator.Emit(OperationCode.Stelem_I1);
            ilGenerator.Emit(OperationCode.Ldloc, loopIndex);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Add);
            ilGenerator.Emit(OperationCode.Stloc, loopIndex);
            ilGenerator.MarkLabel(loopStart);
            ilGenerator.Emit(OperationCode.Ldloc, loopIndex);
            ilGenerator.Emit(OperationCode.Ldloc, byteArray);
            ilGenerator.Emit(OperationCode.Ldlen);
            ilGenerator.Emit(OperationCode.Conv_I4);
            ilGenerator.Emit(OperationCode.Ldc_I4_1);
            ilGenerator.Emit(OperationCode.Sub);
            ilGenerator.Emit(OperationCode.Blt_S, loopBackEdge);
            ilGenerator.Emit(OperationCode.Ldloc, byteArray);
            ilGenerator.Emit(OperationCode.Ldc_I4_0);
            ilGenerator.Emit(OperationCode.Ldelema, byteType);
            ilGenerator.Emit(OperationCode.Stloc, pinnedLocal);
            ilGenerator.Emit(OperationCode.Ldloc, pinnedLocal);
            ilGenerator.Emit(OperationCode.Conv_I);
            ilGenerator.Emit(OperationCode.Br_S, methodExitLabel);
            ilGenerator.MarkLabel(nullLabelCase);
            ilGenerator.Emit(OperationCode.Ldnull);
            ilGenerator.Emit(OperationCode.Conv_I);
            ilGenerator.MarkLabel(methodExitLabel);
        }

        private static void EmitStringReturnMarshalling(ILGenerator ilGenerator)
        {
        }

        private static void EmitBooleanMarshalling(ILGenerator ilGenerator)
        {
        }

        private static void EmitBooleanReturnMarshalling(ILGenerator ilGenerator)
        {
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
                IsReference = false,
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

        private static INamespaceTypeReference CreateTypeReference(IMetadataHost host, IAssemblyReference assemblyReference, string typeName)
        {
            IUnitNamespaceReference ns = new Microsoft.Cci.Immutable.RootUnitNamespaceReference(assemblyReference);
            var names = typeName.Split('.');
            for (int i = 0, n = names.Length - 1; i < n; ++i)
            {
                ns = new Microsoft.Cci.Immutable.NestedUnitNamespaceReference(ns, host.NameTable.GetNameFor(names[i]));
            }

            return new Microsoft.Cci.Immutable.NamespaceTypeReference(host, ns, host.NameTable.GetNameFor(names[names.Length - 1]), 0, isEnum: false, isValueType: false);
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
            ilGenerator.Emit(OperationCode.Ret);

            var ilMethodBody = new ILGeneratorMethodBody(ilGenerator, true, (ushort)((methodDefinition.ParameterCount + 1) * 2), methodDefinition, locals, new List<ITypeDefinition>());
            methodDefinition.Body = ilMethodBody;
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

                if (parameter.Type is IArrayType)
                {
                    EmitArrayMarshalling(locals, ilGenerator, (IArrayType)parameter.Type);
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
                        EmitUnicodeStringMarshalling(locals, ilGenerator, this.intPtrSize, this.host.PlatformType.SystemString);
                    }
                    else
                    {
                        EmitAnsiStringMarshalling(locals, ilGenerator, parameter, this.getLength, this.getChars, this.host.PlatformType);
                    }
                }
            }
        }
    }
}