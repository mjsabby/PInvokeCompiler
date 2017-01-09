//-----------------------------------------------------------------------
// <copyright file="IMethodTransformationMetadataProvider.cs" company="Microsoft">
//     Copyright (c) Microsoft. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace PInvokeCompiler
{
    using Microsoft.Cci;

    internal interface IMethodTransformationMetadataProvider
    {
        IMethodTransformationMetadata Retrieve(IMethodDefinition methodDefinition);
    }
}