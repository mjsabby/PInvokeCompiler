namespace PInvokeCompiler
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
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

                ITypeReference operatingSystemTypeReference = null;
                ITypeReference environmentTypeReference = null;
                ITypeReference platformIdTypeReference = null;
                ITypeReference marshalTypeReference = null;
                ITypeReference runtimeHelpersTypeReference = null;

                foreach (var typeRef in assembly.GetTypeReferences())
                {
                    var name = TypeHelper.GetTypeName(typeRef);
                    if (string.Equals(name, "System.OperatingSystem", StringComparison.Ordinal))
                    {
                        operatingSystemTypeReference = typeRef;
                        continue;
                    }

                    if (string.Equals(name, "System.Environment", StringComparison.Ordinal))
                    {
                        environmentTypeReference = typeRef;
                        continue;
                    }

                    if (string.Equals(name, "System.PlatformID", StringComparison.Ordinal))
                    {
                        platformIdTypeReference = typeRef;
                    }

                    if (string.Equals(name, "System.Runtime.InteropServices.Marshal", StringComparison.Ordinal))
                    {
                        marshalTypeReference = typeRef;
                    }

                    if (string.Equals(name, "System.Runtime.CompilerServices.RuntimeHelpers", StringComparison.Ordinal))
                    {
                        runtimeHelpersTypeReference = typeRef;
                    }
                }

                var mscorlibAsmRef = mutable.AssemblyReferences.FirstOrDefault(t => t.Name.Value.Equals("mscorlib"));
                if (mscorlibAsmRef == null)
                {
                    var tempRef = new AssemblyReference
                    {
                        Name = host.NameTable.GetNameFor("mscorlib"),
                        Version = new Version(4, 0, 0, 0),
                        PublicKeyToken = new List<byte> { 0xB7, 0x7A, 0x5C, 0x56, 0x19, 0x34, 0xE0, 0x89 }
                    };

                    mscorlibAsmRef = tempRef;
                    mutable.AssemblyReferences.Add(mscorlibAsmRef);
                }

                if (operatingSystemTypeReference == null)
                {
                    operatingSystemTypeReference = CreateTypeReference(host, mscorlibAsmRef, "System.OperatingSystem");
                }

                if (environmentTypeReference == null)
                {
                    environmentTypeReference = CreateTypeReference(host, mscorlibAsmRef, "System.Environment");
                }

                if (platformIdTypeReference == null)
                {
                    platformIdTypeReference = CreateTypeReference(host, mscorlibAsmRef, "System.PlatformID", isValueType: true);
                }

                if (marshalTypeReference == null)
                {
                    var interopServicesAssembly = mutable.AssemblyReferences.FirstOrDefault(t => t.Name.Value.Equals("System.Runtime.InteropServices")) ?? mscorlibAsmRef;
                    marshalTypeReference = CreateTypeReference(host, interopServicesAssembly, "System.Runtime.InteropServices.Marshal");
                }

                if (runtimeHelpersTypeReference == null)
                {
                    var runtimeAssembly = mutable.AssemblyReferences.FirstOrDefault(t => t.Name.Value.Equals("System.Runtime")) ?? mscorlibAsmRef;
                    runtimeHelpersTypeReference = CreateTypeReference(host, runtimeAssembly, "System.Runtime.CompilerServices.RuntimeHelpers");
                }

                var getOSVersion = new Microsoft.Cci.MutableCodeModel.MethodReference
                {
                    Name = host.NameTable.GetNameFor("get_OSVersion"),
                    ContainingType = environmentTypeReference,
                    Type = operatingSystemTypeReference
                };

                var getPlatform = new Microsoft.Cci.MutableCodeModel.MethodReference
                {
                    Name = host.NameTable.GetNameFor("get_Platform"),
                    ContainingType = operatingSystemTypeReference,
                    CallingConvention = CallingConvention.HasThis,
                    Type = platformIdTypeReference,
                };

                var pinvokeMethodMetadataTraverser = new PInvokeMethodMetadataTraverser();
                pinvokeMethodMetadataTraverser.TraverseChildren(mutable);

                var loadLibraryMethod = new MethodDefinition();
                var getProcAddressMethod = new MethodDefinition();
                var stringToAnsiByteArrayMethod = new MethodDefinition();

                var methodTransformationMetadataRewriter = new MethodTransformationMetadataRewriter(loadLibraryMethod, getProcAddressMethod, host, pinvokeMethodMetadataTraverser);
                methodTransformationMetadataRewriter.RewriteChildren(mutable);
                
                var pinvokeMethodMetadataRewriter = new PInvokeMethodMetadataRewriter(marshalTypeReference, runtimeHelpersTypeReference, stringToAnsiByteArrayMethod, host, host.PlatformType, host.NameTable, methodTransformationMetadataRewriter);
                pinvokeMethodMetadataRewriter.RewriteChildren(mutable);

                new PlatformSpecificHelpersTypeAdder(host, marshalTypeReference, getOSVersion, getPlatform, loadLibraryMethod, getProcAddressMethod, stringToAnsiByteArrayMethod).RewriteChildren(mutable);

                using (var stream = File.Create(outputFile))
                {
                    PeWriter.WritePeToStream(mutable, host, stream);
                }
            }
        }
        
        private static INamespaceTypeReference CreateTypeReference(IMetadataHost host, IAssemblyReference assemblyReference, string typeName, bool isValueType = false, bool isEnum = false)
        {
            IUnitNamespaceReference ns = new Microsoft.Cci.Immutable.RootUnitNamespaceReference(assemblyReference);
            var names = typeName.Split('.');
            for (int i = 0, n = names.Length - 1; i < n; ++i)
            {
                ns = new Microsoft.Cci.Immutable.NestedUnitNamespaceReference(ns, host.NameTable.GetNameFor(names[i]));
            }

            return new Microsoft.Cci.Immutable.NamespaceTypeReference(host, ns, host.NameTable.GetNameFor(names[names.Length - 1]), 0, isEnum: isEnum, isValueType: isValueType);
        }
    }
}