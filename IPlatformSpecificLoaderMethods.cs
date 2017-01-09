//-----------------------------------------------------------------------
// <copyright file="IPlatformSpecificLoaderMethods.cs" company="Microsoft">
//     Copyright (c) Microsoft. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace PInvokeCompiler
{
    using Microsoft.Cci;

    internal interface IPlatformSpecificLoaderMethods
    {
        IMethodReference LoadLibrary { get; set; }

        IMethodReference GetProcAddress { get; set; }

        IMethodReference FreeLibrary { get; set;  }
    }
}