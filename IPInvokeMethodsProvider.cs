namespace PInvokeRewriter
{
    using System.Collections.Generic;
    using Microsoft.Cci;

    internal interface IPInvokeMethodsProvider
    {
        IEnumerable<IMethodDefinition> Retrieve(ITypeDefinition typeDefinition);
    }
}