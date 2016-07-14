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
                }

                var mscorlibAsmRef = mutable.AssemblyReferences.FirstOrDefault(t => t.Name.Value.Equals("mscorlib"));
                if (mscorlibAsmRef == null)
                {
                    var tempRef = new AssemblyReference
                    {
                        Name = host.NameTable.GetNameFor("mscorlib"),
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
                    Type = platformIdTypeReference,
                };

                var pinvokeHelpersTypesAdder = new PInvokeInteropHelpersTypeAdder(host);
                pinvokeHelpersTypesAdder.RewriteChildren(mutable);

                var pinvokeMethodMetadataTraverser = new PInvokeMethodMetadataTraverser();
                pinvokeMethodMetadataTraverser.TraverseChildren(mutable);

                var methodTransformationMetadataRewriter = new MethodTransformationMetadataRewriter(pinvokeHelpersTypesAdder.LoadLibrary, pinvokeHelpersTypesAdder.GetProcAddress, host, pinvokeMethodMetadataTraverser);
                methodTransformationMetadataRewriter.RewriteChildren(mutable);
                
                var pinvokeMethodMetadataRewriter = new PInvokeMethodMetadataRewriter(marshalTypeReference, host, host.PlatformType, host.NameTable, methodTransformationMetadataRewriter);
                pinvokeMethodMetadataRewriter.RewriteChildren(mutable);

                new PlatformSpecificHelpersTypeAdder(host, marshalTypeReference, getOSVersion, getPlatform).RewriteChildren(mutable);

                using (var stream = File.Create(outputFile))
                {
                    PeWriter.WritePeToStream(mutable, host, stream);
                }
            }
        }

        private sealed class PInvokeInteropHelpersTypeAdder : MetadataRewriter
        {
            public PInvokeInteropHelpersTypeAdder(IMetadataHost host, bool copyAndRewriteImmutableReferences = false)
                : base(host, copyAndRewriteImmutableReferences)
            {
            }

            public IMethodDefinition LoadLibrary { get; private set; }

            public IMethodDefinition GetProcAddress { get; private set; }

            public override void RewriteChildren(Assembly module)
            {
                var typeDef = new NamespaceTypeDefinition();

                this.LoadLibrary = new MethodDefinition
                {
                    ContainingTypeDefinition = typeDef,
                    Type = this.host.PlatformType.SystemIntPtr,
                    Parameters = new List<IParameterDefinition>
                    {
                        new ParameterDefinition { Type = this.host.PlatformType.SystemString }
                    },
                    IsStatic = true,
                    Name = this.host.NameTable.GetNameFor("LoadLibrary"),
                    Visibility = TypeMemberVisibility.Assembly
                };

                var freeLibrary = new MethodDefinition
                {
                    ContainingTypeDefinition = typeDef,
                    Type = this.host.PlatformType.SystemInt32,
                    Parameters = new List<IParameterDefinition>
                    {
                        new ParameterDefinition { Type = this.host.PlatformType.SystemIntPtr }
                    },
                    IsStatic = true,
                    Name = this.host.NameTable.GetNameFor("FreeLibrary"),
                    Visibility = TypeMemberVisibility.Assembly
                };

                this.GetProcAddress = new MethodDefinition
                {
                    ContainingTypeDefinition = typeDef,
                    Type = this.host.PlatformType.SystemIntPtr,
                    Parameters = new List<IParameterDefinition>
                    {
                        new ParameterDefinition { Type = this.host.PlatformType.SystemIntPtr },
                        new ParameterDefinition { Type = this.host.PlatformType.SystemString }
                    },
                    IsStatic = true,
                    Name = this.host.NameTable.GetNameFor("GetProcAddress"),
                    Visibility = TypeMemberVisibility.Assembly
                };

                typeDef.Methods = new List<IMethodDefinition> { this.LoadLibrary, freeLibrary, this.GetProcAddress };

                var rootUnitNamespace = new RootUnitNamespace();
                rootUnitNamespace.Members.AddRange(module.UnitNamespaceRoot.Members);
                module.UnitNamespaceRoot = rootUnitNamespace;
                rootUnitNamespace.Unit = module;

                typeDef.ContainingUnitNamespace = rootUnitNamespace;
                typeDef.IsPublic = true;
                typeDef.BaseClasses = new List<ITypeReference> { this.host.PlatformType.SystemObject };
                typeDef.Name = this.host.NameTable.GetNameFor("PInvokeInteropHelpers");

                rootUnitNamespace.Members.Add(typeDef);
                module.AllTypes.Add(typeDef);

                base.RewriteChildren(module);
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