namespace PInvokeCompiler
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Cci;

    internal sealed class PInvokeMethodMetadataTraverser : MetadataTraverser, IPInvokeMethodsProvider
    {
        private readonly Dictionary<ITypeDefinition, List<IMethodDefinition>> typeDefinitionTable = new Dictionary<ITypeDefinition, List<IMethodDefinition>>();

        private readonly Dictionary<ITypeDefinition, HashSet<IModuleReference>> moduleRefsTable = new Dictionary<ITypeDefinition, HashSet<IModuleReference>>();

        public override void TraverseChildren(IMethodDefinition methodDefinition)
        {
            if (methodDefinition.IsPlatformInvoke)
            {
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
    }
}