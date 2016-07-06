namespace PInvokeCompiler
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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

                Assembly mutable = new MetadataDeepCopier(host).Copy(assembly);

                var pinvokeHelpersTypesAdder = new PInvokeInteropHelpersTypeAdder(host);
                pinvokeHelpersTypesAdder.RewriteChildren(mutable);

                var pinvokeMethodMetadataTraverser = new PInvokeMethodMetadataTraverser();
                pinvokeMethodMetadataTraverser.TraverseChildren(mutable);

                var methodTransformationMetadataRewriter = new MethodTransformationMetadataRewriter(pinvokeHelpersTypesAdder.LoadLibrary, pinvokeHelpersTypesAdder.GetProcAddress, host, host.PlatformType, host.NameTable, pinvokeMethodMetadataTraverser);
                methodTransformationMetadataRewriter.RewriteChildren(mutable);

                var pinvokeMethodMetadataRewriter = new PInvokeMethodMetadataRewriter(mutable.AssemblyReferences, host, host.PlatformType, host.NameTable, methodTransformationMetadataRewriter);
                pinvokeMethodMetadataRewriter.RewriteChildren(mutable);

                assembly = mutable;

                using (var stream = File.Create(outputFile))
                {
                    PeWriter.WritePeToStream(assembly, host, stream);
                }
            }

            using (var host = new PeReader.DefaultHost())
            {
                var unit = host.LoadUnitFrom(outputFile);
                var assembly = unit as IAssembly;

                Assembly mutable = new MetadataDeepCopier(host).Copy(assembly);

                new PInvokeInteropHelpersAssemblyReferenceRemover(host).RewriteChildren(mutable);

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

                /*
                var stream = System.Reflection.IntrospectionExtensions.GetTypeInfo(typeof(Program)).Assembly.GetManifestResourceStream("PInvokeRewriter.PInvokeInteropHelpers.dll") as UnmanagedMemoryStream;
                using (var streamHost = new PeStreamHost(stream))
                {
                    var unit = streamHost.LoadUnitFrom(string.Empty) as IAssembly;
                    if (unit != null)
                    {
                        var x = unit.GetAllTypes().Skip(1);

                        foreach (var t in x)
                        {
                            module.AllTypes.Add(t);
                            rootUnitNamespace.Members.Add((INamespaceMember)t);
                        }
                    }
                }*/

                base.RewriteChildren(module);
            }
        }

        private sealed class PInvokeInteropHelpersAssemblyReferenceRemover : MetadataRewriter
        {
            public PInvokeInteropHelpersAssemblyReferenceRemover(IMetadataHost host, bool copyAndRewriteImmutableReferences = true)
                : base(host, copyAndRewriteImmutableReferences)
            {
            }

            public override void RewriteChildren(Assembly assembly)
            {
                var assemblyReferences = assembly.AssemblyReferences;

                for (int i = 0; i < assemblyReferences.Count; ++i)
                {
                    if (assemblyReferences[i].Name.Value.Equals("PInvokeInteropHelpers"))
                    {
                        assemblyReferences.RemoveAt(i);
                    }
                }

                base.RewriteChildren(assembly);
            }
        }
    }
}