//-----------------------------------------------------------------------
// <copyright file="IPInvokeMethodsProvider.cs" company="Microsoft">
//     Copyright (c) Microsoft. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace PInvokeCompiler
{
    using System.Collections.Generic;
    using Microsoft.Cci;

    internal interface IPInvokeMethodsProvider
    {
        IEnumerable<IMethodDefinition> RetrieveMethodDefinitions(ITypeDefinition typeDefinition);

        IEnumerable<IModuleReference> RetrieveModuleRefs(ITypeDefinition typeDefinition);
    }
}