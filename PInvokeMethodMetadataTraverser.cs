namespace PInvokeRewriter
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Cci;

    internal sealed class PInvokeMethodMetadataTraverser : MetadataTraverser, IPInvokeMethodsProvider
    {
        private readonly Dictionary<ITypeDefinition, List<IMethodDefinition>> typeDefinitionTable = new Dictionary<ITypeDefinition, List<IMethodDefinition>>();

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

                methodDefinitions.Add(methodDefinition);
            }
        }

        public IEnumerable<IMethodDefinition> Retrieve(ITypeDefinition typeDefinition)
        {
            List<IMethodDefinition> methods;
            return this.typeDefinitionTable.TryGetValue(typeDefinition, out methods) ? methods : Enumerable.Empty<IMethodDefinition>();
        }
    }
}