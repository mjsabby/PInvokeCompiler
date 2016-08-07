namespace PInvokeCompiler
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using Microsoft.Cci;
    using Microsoft.Cci.MutableCodeModel;

    internal sealed class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: PInvokeCompiler Input.dll Output.dll");
                return;
            }

            var inputFile = args[0];
            var outputFile = args[1];

            using (var host = new PeReader.DefaultHost())
            {
                var unit = host.LoadUnitFrom(inputFile);
                var assembly = unit as IAssembly;

                if (assembly == null)
                {
                    Console.WriteLine("Error: Input file not loadable as assembly");
                    return;
                }

                Assembly mutable = new MetadataDeepCopier(host).Copy(assembly);                

                var interopHelperReference = new InteropHelperReferences(host, mutable);

                var pinvokeMethodMetadataTraverser = new PInvokeMethodMetadataTraverser(interopHelperReference.PInvokeHelpers);
                pinvokeMethodMetadataTraverser.TraverseChildren(mutable);
                
                var methodTransformationMetadataRewriter = new MethodTransformationMetadataRewriter(interopHelperReference.LoadLibrary, interopHelperReference.GetProcAddress, interopHelperReference.IsLibraryInitialized, host, pinvokeMethodMetadataTraverser);
                methodTransformationMetadataRewriter.RewriteChildren(mutable);
                
                var pinvokeMethodMetadataRewriter = new PInvokeMethodMetadataRewriter(interopHelperReference, host, methodTransformationMetadataRewriter);
                pinvokeMethodMetadataRewriter.RewriteChildren(mutable);

                PdbReader pdbReader = null;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    string pdbFile = Path.ChangeExtension(inputFile, "pdb");
                    if (File.Exists(pdbFile))
                    {
                        using (var pdbStream = File.OpenRead(pdbFile))
                        {
                            pdbReader = new PdbReader(pdbStream, host);
                        }
                    }
                }

                using (pdbReader)
                {
                    using (var stream = File.Create(outputFile))
                    {
                        if (pdbReader == null)
                        {
                            PeWriter.WritePeToStream(mutable, host, stream);
                        }
                        else
                        {
                            var localScopeProvider = new ILGenerator.LocalScopeProvider(pdbReader);
                            using (var pdbWriter = new PdbWriter(Path.ChangeExtension(outputFile, "pdb"), pdbReader))
                            {
                                PeWriter.WritePeToStream(mutable, host, stream, pdbReader, localScopeProvider, pdbWriter);
                            }
                        }
                    }
                }
            }
        }
    }
}