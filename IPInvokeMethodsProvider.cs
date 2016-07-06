namespace PInvokeCompiler
{
    using System.Collections.Generic;
    using Microsoft.Cci;

    internal interface IPInvokeMethodsProvider
    {
        IEnumerable<IMethodDefinition> RetrieveMethodDefinitions(ITypeDefinition typeDefinition);

        IEnumerable<string> RetrieveModuleRefs(ITypeDefinition typeDefinition);
    }
}