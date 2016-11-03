namespace PInvokeCompiler
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

                if (!methodDefinition.Parameters.All(IsParameterSupported))
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
        
        private static bool IsReturnTypeSupported(IMethodDefinition methodDefinition)
        {
            if (methodDefinition.ReturnValueIsMarshalledExplicitly)
            {
                var unmanagedType = methodDefinition.ReturnValueMarshallingInformation.UnmanagedType;
                return methodDefinition.Type.IsString() && (unmanagedType == UnmanagedType.LPWStr || unmanagedType == UnmanagedType.LPStr);
            }

            var returnType = methodDefinition.Type;
            if (returnType.TypeCode == PrimitiveTypeCode.Boolean || returnType.IsBlittable() || returnType.IsDelegate() || returnType.IsString())
            {
                return true;
            }

            return false;
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
                        return parameterType.IsString() || parameterType.IsStringArray();
                    case UnmanagedType.LPArray:
                        if (parameterType.IsBlittable())
                        {
                            return true;
                        }

                        if (parameterType.IsStringArray())
                        {
                            var elementType = parameterDefinition.MarshallingInformation.ElementType;
                            if (elementType == UnmanagedType.LPStr || elementType == UnmanagedType.LPWStr)
                            {
                                return true;
                            }
                        }

                        return false;
                }
            }

            // blittable, delegates and strings -- these last two have special marshalling we take care of
            if (parameterType.TypeCode == PrimitiveTypeCode.Boolean || parameterType.IsBlittable() || parameterType.IsDelegate() || parameterType.IsString())
            {
                return true;
            }

            // we also support string[] since it's so common, by converting it to IntPtr[] in a try/finally
            if (parameterType.IsStringArray())
            {
                return true;
            }

            // TODO: Support ICustomMarshaler

            return false;
        }
        
    }
}