namespace PInvokeCompiler
{
    using Microsoft.Cci;

    internal sealed class MethodTransformationMetadata : IMethodTransformationMetadata
    {
        public MethodTransformationMetadata(IMethodDefinition initMethodDefinition, IFieldDefinition functionPointerDefinition, IMethodDefinition nativeMethodDefinition)
        {
            this.InitializeMethod = initMethodDefinition;
            this.FunctionPointer = functionPointerDefinition;
            this.NativeMethod = nativeMethodDefinition;
        }

        public IMethodDefinition NativeMethod { get; }

        public IMethodDefinition InitializeMethod { get; }

        public IFieldDefinition FunctionPointer { get; }
    }
}