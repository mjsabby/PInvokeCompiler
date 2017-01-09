//-----------------------------------------------------------------------
// <copyright file="UnixLoaderMethods.cs" company="Microsoft">
//     Copyright (c) Microsoft. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace PInvokeCompiler
{
    using Microsoft.Cci;

    internal sealed class UnixLoaderMethods : IUnixLoaderMethods
    {
        public IMethodReference LoadLibrary { get; set; }

        public IMethodReference GetProcAddress { get; set; }

        public IMethodReference FreeLibrary { get; set; }
    }
}