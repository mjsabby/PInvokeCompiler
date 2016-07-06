namespace PInvokeCompiler
{
    using Microsoft.Cci;

    internal interface IMethodTransformationMetadataProvider
    {
        IMethodTransformationMetadata Retrieve(IMethodDefinition methodDefinition);
    }
}