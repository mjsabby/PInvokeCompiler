namespace PInvokeRewriter
{
    using Microsoft.Cci;

    internal interface IMethodTransformationMetadataProvider
    {
        IMethodTransformationMetadata Retrieve(IMethodDefinition methodDefinition);
    }
}