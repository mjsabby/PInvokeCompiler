//-----------------------------------------------------------------------
// <copyright file="IMethodTransformationMetadata.cs" company="Microsoft">
//     Copyright (c) Microsoft. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace PInvokeCompiler
{
    using Microsoft.Cci;

    internal interface IMethodTransformationMetadata
    {
        IMethodDefinition InitializeMethod { get; }

        IFieldDefinition FunctionPointer { get; }

        IMethodDefinition NativeMethod { get; }
    }
}