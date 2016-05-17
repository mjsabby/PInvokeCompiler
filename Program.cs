using System.Collections.Generic;

namespace PInvokeRewriter
{
    using System;
    using System.IO;
    using System.Linq;
    using Microsoft.Cci;
    using Microsoft.Cci.MutableCodeModel;
    
    internal sealed class Program
    {
        static void Main(string[] args)
        {
            using (var host = new PeReader.DefaultHost())
            {
                var unit = host.LoadUnitFrom(args[0]);
                var assembly = unit as IAssembly;
                
                Assembly mutable = new MetadataDeepCopier(host).Copy(assembly);

                var xx = new MyRewriter(host);
                xx.RewriteChildren(mutable);
                


                var pinvokeMethodMetadataTraverser = new PInvokeMethodMetadataTraverser();
                pinvokeMethodMetadataTraverser.TraverseChildren(mutable);

                var methodTransformationMetadataRewriter = new MethodTransformationMetadataRewriter(mutable.AssemblyReferences, host, host.PlatformType, host.NameTable, pinvokeMethodMetadataTraverser);
                methodTransformationMetadataRewriter.RewriteChildren(mutable);

                var pinvokeMethodMetadataRewriter = new PInvokeMethodMetadataRewriter(mutable.AssemblyReferences, host, host.PlatformType, host.NameTable, methodTransformationMetadataRewriter);
                pinvokeMethodMetadataRewriter.RewriteChildren(mutable);

                assembly = mutable;
                
                using (var outputFile = File.Create(@"C:\users\mukul\desktop\foo.dll"))
                {
                    PeWriter.WritePeToStream(assembly, host, outputFile);
                }
            }
        }

        internal class MyRewriter : MetadataRewriter
        {
            public MyRewriter(IMetadataHost host, bool copyAndRewriteImmutableReferences = false) : base(host, copyAndRewriteImmutableReferences)
            {
            }

            public override List<IModule> Rewrite(List<IModule> modules)
            {
                return base.Rewrite(modules);
            }

            public override void RewriteChildren(Unit unit)
            {
                base.RewriteChildren(unit);
            }
            
            public override void RewriteChildren(Module module)
            {


                var selfAssembly = typeof(Program).Assembly;
                var stream = selfAssembly.GetManifestResourceStream("PInvokeRewriter.PInvokeInteropHelpers2.dll") as UnmanagedMemoryStream;

                var host2 = new MyHost(stream);
                var unit2 = host2.LoadUnitFrom(string.Empty) as IAssembly;

                var types = unit2.GetAllTypes();
                foreach (var type in types)
                {
                    //type.
                }


                //module.AllTypes.AddRange(unit2.GetAllTypes().Skip(1));

                base.RewriteChildren(module);
            }
            
            public override List<IAssemblyReference> Rewrite(List<IAssemblyReference> assemblyReferences)
            {
                return base.Rewrite(assemblyReferences);
            }

            public override void RewriteChildren(Assembly assembly)
            {

                var asmRef = new AssemblyReference();
                asmRef.AssemblyIdentity = new AssemblyIdentity(this.host.NameTable.GetNameFor("PInvokeInteropHelpers"), "neutral", new Version(1, 0, 0, 0), new List<byte>(), @"C:\Users\Mukul\Desktop\PInvokeInteropHelpers\bin\Debug\PInvokeInteropHelpers.dll");
                asmRef.Name = this.host.NameTable.GetNameFor("PInvokeInteropHelpers");


                assembly.AssemblyReferences.Add(asmRef);
                base.RewriteChildren(assembly);
            }
        }

        private sealed class MyHost : PeReader.DefaultHost
        {
            private readonly BinaryDocumentMemoryBlock block;

            private readonly InMemoryBinaryDocument document;

            public MyHost(UnmanagedMemoryStream stream)
            {
                this.document = new InMemoryBinaryDocument((uint)stream.Length);
                this.block = new BinaryDocumentMemoryBlock(stream, this.document);
            }

            public override IUnit LoadUnitFrom(string location)
            {
                return this.peReader.OpenModule(this.document);
            }

            public override IBinaryDocumentMemoryBlock OpenBinaryDocument(IBinaryDocument sourceDocument)
            {
                return this.block;
            }

            private sealed class InMemoryBinaryDocument : IBinaryDocument
            {
                public InMemoryBinaryDocument(uint length)
                {
                    this.Location = string.Empty;
                    this.Length = length;
                }

                public string Location { get; }

                public uint Length { get; }

                public IName Name
                {
                    get { throw new NotImplementedException(); }
                }
            }

            private sealed class BinaryDocumentMemoryBlock : IBinaryDocumentMemoryBlock
            {
                private readonly UnmanagedMemoryStream stream;

                private readonly InMemoryBinaryDocument binaryDocument;

                public BinaryDocumentMemoryBlock(UnmanagedMemoryStream stream, InMemoryBinaryDocument binaryDocument)
                {
                    this.stream = stream;
                    this.binaryDocument = binaryDocument;
                }

                public IBinaryDocument BinaryDocument
                {
                    get { return this.binaryDocument; }
                }

                public unsafe byte* Pointer
                {
                    get { return this.stream.PositionPointer; }
                }

                public uint Length
                {
                    get { return (uint)this.stream.Length; }
                }
            }
        }
    }
}