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