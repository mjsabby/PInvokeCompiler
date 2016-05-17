using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Cci;
using Microsoft.Cci.MutableCodeModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
    delegate void foo();

    [TestClass]
    public class UnitTest1
    {
        public unsafe byte* Method(byte[] arr, byte* a1, bool xyz, string xdd, UnitTest1 dddd)
        {
            throw new AccessViolationException();
        }

        [TestMethod]
        public void TestMethod1()
        {
            using (var host = new PeReader.DefaultHost())
            {
                var unit = host.LoadUnitFrom(@"C:\Users\Mukul\Desktop\PInvokeRewriter\UnitTests\bin\Debug\UnitTests.dll");
                var assembly = unit as IAssembly;

                var list = assembly.GetAllTypes().ToList();
                var methods = list[2].Methods.ToList();

                var xx = new VectorTypeReference
                {
                    ElementType = host.PlatformType.SystemUInt8,
                    Rank = 1
                };


                var listx = methods[0].Parameters.ToList();

                var xxxx = methods[0].Parameters.ToList()[0].Type as IArrayTypeReference;

                var xxxxxx = TypeHelper.ArrayTypesAreEquivalent(xx, xxxx, true);

                var retType = methods[0].Type;

                var x = methods[0].Parameters.ToList()[0].Type as IArrayType;
                Debug.WriteLine(retType.IsValueType);
            }
        }
    }
}
