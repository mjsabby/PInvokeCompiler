﻿namespace PInvokeCompiler
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices;
    using Microsoft.Cci;

    internal sealed class PInvokeMethodMetadataTraverser : MetadataTraverser, IPInvokeMethodsProvider
    {
        private readonly Dictionary<ITypeDefinition, List<IMethodDefinition>> typeDefinitionTable = new Dictionary<ITypeDefinition, List<IMethodDefinition>>();

        private readonly Dictionary<ITypeDefinition, HashSet<IModuleReference>> moduleRefsTable = new Dictionary<ITypeDefinition, HashSet<IModuleReference>>();

        private readonly ITypeReference skipTypeReference;

        public PInvokeMethodMetadataTraverser(ITypeReference skipTypeReference)
        {
            this.skipTypeReference = skipTypeReference;
        }

        public override void TraverseChildren(IMethodDefinition methodDefinition)
        {
            if (methodDefinition.IsPlatformInvoke)
            {
                if (TypeHelper.TypesAreEquivalent(methodDefinition.ContainingTypeDefinition, this.skipTypeReference))
                {
                    return;
                }

                if (!IsReturnTypeSupported(methodDefinition))
                {
                    throw new Exception($"Return type {methodDefinition.Type} is not supported for marshalling");   
                }

                if (!AreParametersSupported(methodDefinition))
                {
                    throw new Exception($"Parameter types {methodDefinition} are not supported for marshalling");
                }

                var typeDefinition = methodDefinition.ContainingTypeDefinition;
                List<IMethodDefinition> methodDefinitions;
                if (!this.typeDefinitionTable.TryGetValue(typeDefinition, out methodDefinitions))
                {
                    methodDefinitions = new List<IMethodDefinition>();
                    this.typeDefinitionTable.Add(typeDefinition, methodDefinitions);
                }

                HashSet<IModuleReference> moduleRefs;
                if (!this.moduleRefsTable.TryGetValue(typeDefinition, out moduleRefs))
                {
                    moduleRefs = new HashSet<IModuleReference>();
                    this.moduleRefsTable.Add(typeDefinition, moduleRefs);
                }

                methodDefinitions.Add(methodDefinition);
                moduleRefs.Add(methodDefinition.PlatformInvokeData.ImportModule);
            }
        }

        public IEnumerable<IMethodDefinition> RetrieveMethodDefinitions(ITypeDefinition typeDefinition)
        {
            List<IMethodDefinition> methods;
            return this.typeDefinitionTable.TryGetValue(typeDefinition, out methods) ? methods : Enumerable.Empty<IMethodDefinition>();
        }

        public IEnumerable<IModuleReference> RetrieveModuleRefs(ITypeDefinition typeDefinition)
        {
            HashSet<IModuleReference> moduleRefs;
            return this.moduleRefsTable.TryGetValue(typeDefinition, out moduleRefs) ? moduleRefs : Enumerable.Empty<IModuleReference>();
        }

        private static bool IsBlittableType(ITypeReference typeRef)
        {
            if (IsPrimitive(typeRef))
            {
                return true;
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

        private static bool IsPrimitive(ITypeReference typeRef)
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
                default:
                    return false;
            }
        }

        private static bool IsReturnTypeSupported(IMethodDefinition methodDefinition)
        {
            if (methodDefinition.ReturnValueIsMarshalledExplicitly)
            {
                return false;
            }

            var returnType = methodDefinition.Type;
            if (IsBlittableType(returnType) || IsDelegate(returnType) || IsString(returnType))
            {
                return true;
            }

            return false;
        }

        private static bool AreParametersSupported(IMethodDefinition methodDefinition)
        {
            foreach (var parameter in methodDefinition.Parameters)
            {
                if (!IsParameterSupported(parameter))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsParameterSupported(IParameterDefinition parameterDefinition)
        {
            var parameterType = parameterDefinition.Type;
            
            // special short-circuit for specific marshalling.
            if (parameterDefinition.IsMarshalledExplicitly)
            {
                var unmanagedType = parameterDefinition.MarshallingInformation.UnmanagedType;
                switch (unmanagedType)
                {
                    case UnmanagedType.LPWStr:
                    case UnmanagedType.LPStr:
                        return IsString(parameterType);
                    case UnmanagedType.LPArray:
                        return IsBlittableArray(parameterType);
                }
            }

            // primitives, value types, delegates and strings -- these last two have special marshalling we take care of
            if (IsBlittableType(parameterType) || IsDelegate(parameterType) || IsString(parameterType))
            {
                return true;
            }

            // array not being marshalled as LPArray, but it could be a primitive array
            if (IsBlittableArray(parameterType))
            {
                var resolvedType = (IArrayType)parameterType.ResolvedType;
                var elementType = resolvedType.ElementType.ResolvedType;

                if (IsPrimitive(resolvedType) || (elementType.Fields.Count(t => !t.IsStatic) == 1 && IsPrimitive(elementType.Fields.First(t => !t.IsStatic).Type)))
                {
                    return true;
                }

                return false;
            }

            // we also support string[] since it's so common, by converting it to IntPtr[] in a try/finally
            if (IsStringArray(parameterType))
            {
                return true;
            }

            // TODO: Support ICustomMarshaler

            return false;
        }
        
        private static bool IsBlittableArray(ITypeReference typeRef)
        {
            var arrayType = typeRef.ResolvedType as IArrayType;
            var elementType = arrayType?.ElementType;

            if (arrayType?.Rank == 1 && IsBlittableType(elementType))
            {
                return true;
            }

            return false;
        }

        private static bool IsDelegate(ITypeReference typeReference)
        {
            return typeReference.ResolvedType.IsDelegate;
        }

        private static bool IsString(ITypeReference typeReference)
        {
            return typeReference.ToString() == "System.String";
        }

        private static bool IsStringArray(ITypeReference typeRef)
        {
            var arrayType = typeRef.ResolvedType as IArrayType;
            var elementType = arrayType?.ElementType;
            if (arrayType?.Rank == 1 && IsString(elementType))
            {
                return true;
            }

            return false;
        }
    }
}