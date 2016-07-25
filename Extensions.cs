namespace PInvokeCompiler
{
    using System;
    using Microsoft.Cci;

    internal static class Extensions
    {
        public static bool IsBlittable(this ITypeReference typeRef)
        {
            if (typeRef.IsPrimitive())
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

                    if (fieldInfo.IsMarshalledExplicitly || !fieldInfo.Type.IsBlittable())
                    {
                        return false;
                    }
                }

                return true;
            }

            var arrayType = typeRef.ResolvedType as IArrayType;
            var elementType = arrayType?.ElementType;

            if (arrayType?.Rank == 1 && elementType.IsBlittable())
            {
                return true;
            }

            return false;
        }
        
        internal static bool IsPrimitive(this ITypeReference typeRef)
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

        internal static bool IsDelegate(this ITypeReference typeReference)
        {
            return typeReference.ResolvedType.IsDelegate;
        }

        internal static bool IsString(this ITypeReference typeReference)
        {
            return typeReference.ToString() == "System.String";
        }

        internal static bool IsStringArray(this ITypeReference typeRef)
        {
            var arrayType = typeRef.ResolvedType as IArrayType;
            var elementType = arrayType?.ElementType;
            if (arrayType?.Rank == 1 && elementType.IsString())
            {
                return true;
            }

            return false;
        }
    }
}