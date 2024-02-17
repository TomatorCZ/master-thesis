// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public partial class PartialTypeInferenceTests : CompilingTestBase
    {
        #region Helpers
        [Flags]
        internal enum Symbols
        {
            Methods = 1,
            ObjectCreation = 2
        }

        internal static SymbolDisplayFormat TestCallSiteDisplayStringFormat = SymbolDisplayFormat.TestFormat
            .WithParameterOptions(SymbolDisplayFormat.TestFormat.ParameterOptions & ~SymbolDisplayParameterOptions.IncludeName)
            .WithMemberOptions(SymbolDisplayFormat.TestFormat.MemberOptions & ~SymbolDisplayMemberOptions.IncludeType)
            .WithMiscellaneousOptions(SymbolDisplayFormat.TestFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        internal static void TestCallSites(string source, Symbols symbolsToCheck, ImmutableArray<DiagnosticDescription> expectedDiagnostics)
        {
            var compilationOptions = TestOptions.RegularPreview;

            var compilation = CreateCompilation(source, parseOptions: compilationOptions);

            //Verify errors
            compilation.VerifyDiagnostics(expectedDiagnostics.ToArray());

            var tree = compilation.SyntaxTrees.Single();
            var model = compilation.GetSemanticModel(tree);

            //Verify symbols
            var results = string.Join("\n", model.SyntaxTree.GetRoot().DescendantNodesAndSelf().Where(node =>
                    (node is InvocationExpressionSyntax invocation && symbolsToCheck.HasFlag(Symbols.Methods))
                    || (node is ObjectCreationExpressionSyntax && symbolsToCheck.HasFlag(Symbols.ObjectCreation))
                )
                .Select(node => model.GetSymbolInfo(node).Symbol)
                .Where(symbol => symbol != null)
                .Select(symbol => symbol.ToDisplayString(TestCallSiteDisplayStringFormat))
                .ToArray()
            );

            var expected = string.Join("\n", source
                .Split(new[] { Environment.NewLine }, System.StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Contains("//-"))
                .Select(x => x.Substring(x.IndexOf("//-", StringComparison.Ordinal) + 3))
                .ToArray());

            AssertEx.EqualOrDiff(expected, results);
        }
        internal static void TestCallSites(string source, Symbols symbolsToCheck) => TestCallSites(source, symbolsToCheck, ImmutableArray<DiagnosticDescription>.Empty);
        #endregion

        #region PartialMethodTypeInference
        [Fact]
        public void PartialMethodTypeInference_UnderscoreClass()
        {
            TestCallSites("""
class P
{
    static void M() 
    {
        F1<_>(1); //-P.F1<int>(int)
    }

    static void F1<T>(T p) {}
}

class _ {}
""",
                Symbols.Methods,
                ImmutableArray.Create(
                    // (11,7): warning CS9214: Types and aliases should not be named '_'.
                    // class _ {}
                    Diagnostic(ErrorCode.WRN_UnderscoreNamedDisallowed, "_").WithLocation(11, 7)
                )
            );
        }

        [Fact]
        public void PartialMethodTypeInference_Syntax()
        {
            TestCallSites("""
using System;

namespace X;
#nullable enable

class P
{
    static void M() 
    {
        A temp1 = new A();
        F<_>(temp1); //-X.P.F<X.P.A>(X.P.A)
        P.F<_>(temp1); //-X.P.F<X.P.A>(X.P.A)
        global::X.P.F<_>(temp1); //-X.P.F<X.P.A>(X.P.A)

        A? temp2 = null;
        F<_?>(temp2); //-X.P.F<X.P.A?>(X.P.A?)

        A<A?>? temp3 = null;
        F<A<_?>?>(temp3); //-X.P.F<X.P.A<X.P.A?>?>(X.P.A<X.P.A?>?)

        A.B<A> temp4 = new A.B<A>();
        F<global::X.P.A.B<_>>(temp4); //-X.P.F<X.P.A.B<X.P.A>>(X.P.A.B<X.P.A>)
        F<A.B<_>>(temp4); //-X.P.F<X.P.A.B<X.P.A>>(X.P.A.B<X.P.A>)

        A[] temp5 = new A[1];
        F<_[]>(temp5); //-X.P.F<X.P.A[]>(X.P.A[])

        A<A>[] temp6 = new A<A>[1];
        F<A<_>[]>(temp6); //-X.P.F<X.P.A<X.P.A>[]>(X.P.A<X.P.A>[])

        var temp7 = (1, 1);
        F<(_, _)>(temp7); //-X.P.F<(int, int)>((int, int))

        (new B())
        .F<_>(1) //-X.P.B.F<int>(int)
        .F<_>(1) //-X.P.B.F<int>(int)
        .F<_>(1); //-X.P.B.F<int>(int)

        A<_>.F<_>(temp1); //-X.P.A<_>.F<X.P.A>(X.P.A)
    }

    static void F<T>(T p) {}

    class A 
    {
        public class B<T> {}
    }
    class A<T1> 
    {
        public static void F<T2>(T2 p1) {}
    }
    class B 
    {
        public B F<T>(T p) { throw new NotImplementedException(); }  
    }
}
""",
                Symbols.Methods,
                ImmutableArray.Create(
                    // (39,11): error CS0246: The type or namespace name '_' could not be found (are you missing a using directive or an assembly reference?)
                    //         A<_>.F<_>(temp1);
                    Diagnostic(ErrorCode.ERR_SingleTypeNameNotFound, "_").WithArguments("_").WithLocation(39, 11)
                )
            );
        }

        [Fact]
        public void PartialMethodTypeInference_Simple()
        {
            TestCallSites("""
using System;

public class P
{
    public void M() 
    {
        F1<_, string>(1); //-P.F1<int, string>(int)
        F2<_,_>(1,""); //-P.F2<int, string>(int, string)
        F3<int, _, string, _>(new G2<string, string>()); //-P.F3<int, string, string, string>(P.G2<string, string>)
        F4<_, _, string>(x => x + 1, y => y.ToString(),z => z.Length); //-P.F4<int, int, string>(System.Func<int, int>, System.Func<int, string>, System.Func<string, int>)
                                                                       //-int.ToString()
        F5<string>(1); //-P.F5<string>(int, params string[])
        F5<_>(1, ""); //-P.F5<string>(int, params string[])
        F5<_>(1, "", ""); //-P.F5<string>(int, params string[])
    }
    void F1<T1, T2>(T1 p1) {}
    void F2<T1, T2>(T1 p1, T2 p2) {}
    void F2<T1>(T1 p1, string p2) {}
    void F3<T1, T2, T3, T4>(G2<T2, T4> p24) {}
    class G2<T1, T2> {}
    void F4<T1, T2, T3>(Func<T1, T2> p12, Func<T2, T3> p23, Func<T3, T1> p31) { }
    void F5<T>(int p1, params T[] args) {}
}
""",
        Symbols.Methods
    );
        }

        [Fact]
        public void PartialMethodTypeInference_Nested()
        {
            TestCallSites("""
class P
{
    void M() 
    {
        B1<int> temp1 = null;
        F6<A1<_>>(temp1); //-P.F6<P.A1<int>>(P.A1<int>)

        B2<int, string> temp2 = null;
        F6<A2<_, string>>(temp2); //-P.F6<P.A2<int, string>>(P.A2<int, string>)

        C2<int, B> temp3 = null;
        F6<I2<_, A>>(temp3); //-P.F6<P.I2<int, P.A>>(P.I2<int, P.A>)
    }   

    void F6<T1>(T1 p1) {}

    class A {}
    class B : A{}
    class A1<T> {}
    class B1<T> : A1<T> {}
    class A2<T1, T2> {}
    class B2<T1, T2> : A2<T1, T2> {}
    interface I2<in T1, out T2> {}
    class C2<T1, T2> : I2<T1, T2> {}
} 
""",
        Symbols.Methods
    );
        }

        [Fact]
        public void PartialMethodTypeInference_Dynamic()
        {
            TestCallSites("""
class P {
    void M1() 
    {
        dynamic temp4 = "";

        // Warning: Inferred type argument is not supported by runtime (type hints will not be used at all)
        temp4.M<_>();

        // Warning: Inferred type argument is not supported by runtime (type hints will not be used at all)
        F7<string, _>("", temp4, 1); //-P.F7<T1, T2>(T1, T2, T1)
        
        // Warning: Inferred type argument is not supported by runtime (type hints will not be used at all)
        F7<_, string>(1, temp4, 1); //-P.F7<T1, T2>(T1, T2, T1)
        
        // Warning: Inferred type argument is not supported by runtime (type hints will not be used at all)
        temp4.F7<string, _>(temp4);  
    }

    void F7<T1, T2>(T1 p1, T2 p2, T1 p3) {}
}
""",
        Symbols.Methods,
        ImmutableArray.Create(
                // (7,9): warning CS9212: Type hints will not be considered in type inference of dynamic call.
                //         temp4.M<_>();
                Diagnostic(ErrorCode.WRN_TypeHintsInDynamicCall, "temp4.M<_>()").WithLocation(7, 9),
                // (10,9): warning CS9212: Type hints will not be considered in type inference of dynamic call.
                //         F7<string, _>("", temp4, 1);
                Diagnostic(ErrorCode.WRN_TypeHintsInDynamicCall, @"F7<string, _>("""", temp4, 1)").WithLocation(10, 9),
                // (13,9): warning CS9212: Type hints will not be considered in type inference of dynamic call.
                //         F7<_, string>(1, temp4, 1); 
                Diagnostic(ErrorCode.WRN_TypeHintsInDynamicCall, "F7<_, string>(1, temp4, 1)").WithLocation(13, 9),
                // (16,9): warning CS9212: Type hints will not be considered in type inference of dynamic call.
                //         temp4.F7<string, _>(temp4);  
                Diagnostic(ErrorCode.WRN_TypeHintsInDynamicCall, "temp4.F7<string, _>(temp4)").WithLocation(16, 9)
        )
    );
        }

        [Fact]
        public void PartialMethodTypeInference_ErrorRecovery()
        {
            TestCallSites("""
class P {
    void M1() 
    {
        F1<_,_>(""); // Error: Can't infer T2
        F1<int, string>(""); // Error: int != string
        F1<byte,_>(257); // Error: Can't infer T2
    }

    void F1<T1, T2>(T1 p1) {}
}
""",
        Symbols.Methods,
        ImmutableArray.Create(
            // (4,9): error CS0411: The type arguments for method 'P.F1<T1, T2>(T1)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
            //         F1<_,_>(""); // Error: Can't infer T2
            Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1<_,_>").WithArguments("P.F1<T1, T2>(T1)").WithLocation(4, 9),
            // (5,25): error CS1503: Argument 1: cannot convert from 'string' to 'int'
            //         F1<int, string>(""); // Error: int != string
            Diagnostic(ErrorCode.ERR_BadArgType, @"""""").WithArguments("1", "string", "int").WithLocation(5, 25),
            // (6,9): error CS0411: The type arguments for method 'P.F1<T1, T2>(T1)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
            //         F1<byte,_>(257); // Error: Can't infer T2
            Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1<byte,_>").WithArguments("P.F1<T1, T2>(T1)").WithLocation(6, 9)
        )
    );
        }

        [Fact]
        public void PartialMethodTypeInference_Nullable()
        {
            TestCallSites("""
using System;
#nullable enable
class P {
    void M1() 
    {
        string? temp5 = null;
        string? temp5a = null;
        string? temp5b = null;
        string temp6 = "";
        C2<int, string> temp7 = new C2<int, string>();
        C2<int, string?> temp8 = new C2<int, string?>();
        C2<string?, int> temp9 = new C2<string?, int>();
        
        F8<int, _>(temp5); //-P.F8<int, string>(string?)
        F8<int, _>(temp6); //-P.F8<int, string>(string?)
        F8<int?, _>(temp5); //-P.F8<int?, string>(string?)
        F8<int?, _>(temp6); //-P.F8<int?, string>(string?)
        F9<int, _>(temp5a); //-P.F9<int, string?>(string?)
        F9<int, _>(temp6); //-P.F9<int, string>(string)
        F9<int?, _>(temp5b); //-P.F9<int?, string?>(string?)
        F9<int?, _>(temp6); //-P.F9<int?, string>(string)
        
        F10<I2<_, string?>>(temp7); //-P.F10<P.I2<int, string?>>(P.I2<int, string?>)
        //Warning: Can't convert string? to string because of invariance
        F10<C2<_, string?>>(temp7); //-P.F10<P.C2<int, string?>>(P.C2<int, string?>)
        F10<I2<_, _>>(temp7); //-P.F10<P.I2<int, string>>(P.I2<int, string>)
        F10<C2<_, _>>(temp7); //-P.F10<P.C2<int, string>>(P.C2<int, string>)
        F10<I2<_, _?>>(temp8); //-P.F10<P.I2<int, string?>>(P.I2<int, string?>)
        F10<C2<_, _?>>(temp8); //-P.F10<P.C2<int, string?>>(P.C2<int, string?>)
        //Warning: Can't convert string? to string because of covariance
        F10<I2<_, string>>(temp8); //-P.F10<P.I2<int, string>>(P.I2<int, string>)
        //Warning: Can't convert string? to string because of invariance
        F10<C2<_, string>>(temp8); //-P.F10<P.C2<int, string>>(P.C2<int, string>)
        F10<I2<_, int>>(temp9); //-P.F10<P.I2<string?, int>>(P.I2<string?, int>)
        
        F10<_?>("maybe null"); //-P.F10<string?>(string?)
        F10<I2<_, _?>>(temp7); //-P.F10<P.I2<int, string?>>(P.I2<int, string?>)
        F10<_?>(1); //-P.F10<int>(int)
        //Error: Can't be inferred because void F12<T>(Nullable<T> p ) {} and F(1) is not inferred either.
        F10<Nullable<_>>(1);
    }

    interface I2<in T1, out T2> {}
    class C2<T1, T2> : I2<T1, T2> {}

    void F8<T1, T2>(T2? p2) { }
    void F9<T1, T2>(T2 p2) { }
    void F10<T1>(T1 p1) {}
}
""",
        Symbols.Methods,
                       ImmutableArray.Create(
                            // (25,29): warning CS8620: Argument of type 'P.C2<int, string>' cannot be used for parameter 'p1' of type 'P.C2<int, string?>' in 'void P.F10<C2<int, string?>>(C2<int, string?> p1)' due to differences in the nullability of reference types.
                            //         F10<C2<_, string?>>(temp7); //-P.F10<P.C2<int, string?>>(P.C2<int, string?>)
                            Diagnostic(ErrorCode.WRN_NullabilityMismatchInArgument, "temp7").WithArguments("P.C2<int, string>", "P.C2<int, string?>", "p1", "void P.F10<C2<int, string?>>(C2<int, string?> p1)").WithLocation(25, 29),
                            // (31,28): warning CS8620: Argument of type 'P.C2<int, string?>' cannot be used for parameter 'p1' of type 'P.I2<int, string>' in 'void P.F10<I2<int, string>>(I2<int, string> p1)' due to differences in the nullability of reference types.
                            //         F10<I2<_, string>>(temp8); //-P.F10<P.I2<int, string>>(P.I2<int, string>)
                            Diagnostic(ErrorCode.WRN_NullabilityMismatchInArgument, "temp8").WithArguments("P.C2<int, string?>", "P.I2<int, string>", "p1", "void P.F10<I2<int, string>>(I2<int, string> p1)").WithLocation(31, 28),
                            // (33,28): warning CS8620: Argument of type 'P.C2<int, string?>' cannot be used for parameter 'p1' of type 'P.C2<int, string>' in 'void P.F10<C2<int, string>>(C2<int, string> p1)' due to differences in the nullability of reference types.
                            //         F10<C2<_, string>>(temp8); //-P.F10<P.C2<int, string>>(P.C2<int, string>)
                            Diagnostic(ErrorCode.WRN_NullabilityMismatchInArgument, "temp8").WithArguments("P.C2<int, string?>", "P.C2<int, string>", "p1", "void P.F10<C2<int, string>>(C2<int, string> p1)").WithLocation(33, 28),
                            // (40,9): error CS0411: The type arguments for method 'P.F10<T1>(T1)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                            //         F10<Nullable<_>>(1);
                            Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F10<Nullable<_>>").WithArguments("P.F10<T1>(T1)").WithLocation(40, 9)
                       )
    );
        }

        [Fact]
        public void PartialMethodTypeInference_ExtensionMethods()
        {
            TestCallSites("""
public class C {
    public void M() {
        new G<int>().F();
    }
}

public class G<T> {}

public static class A {
    public static void F<T1, T2>(this G<T1> p) {
    }
}
""",
        Symbols.Methods,
                        ImmutableArray.Create(
                            // (3,22): error CS0411: The type arguments for method 'A.F<T1, T2>(G<T1>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                            //         new G<int>().F();
                            Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("A.F<T1, T2>(G<T1>)").WithLocation(3, 22)
                        ));
        }
        #endregion

        #region PartialConstructorTypeInference
        [Fact]
        public void PartialConstructorTypeInference_UnderscoreClass()
        {
            TestCallSites("""
class P
{
    static void M() 
    {
        new F1<_>(1); //-P.F1<int>..ctor(int)
    }

    class F1<T> { public F1(T p) {} }
}

class _ {}
""",
                Symbols.ObjectCreation,
                ImmutableArray.Create(
                    // (11,7): warning CS9214: Types and aliases should not be named '_'.
                    // class _ {}
                    Diagnostic(ErrorCode.WRN_UnderscoreNamedDisallowed, "_").WithLocation(11, 7)
                )
            );
        }

        [Fact]
        public void PartialConstructorTypeInference_Syntax()
        {
            TestCallSites("""
namespace X;
#nullable enable

class P
{
    static void M() 
    {
        A temp1 = new A(); //-X.P.A..ctor()
        new F<_>(temp1); //-X.P.F<X.P.A>..ctor(X.P.A)
        new P.F<_>(temp1); //-X.P.F<X.P.A>..ctor(X.P.A)
        new global::X.P.F<_>(temp1); //-X.P.F<X.P.A>..ctor(X.P.A)

        A? temp2 = null;
        new F<_?>(temp2); //-X.P.F<X.P.A?>..ctor(X.P.A?)

        A<A?>? temp3 = null;
        new F<A<_?>?>(temp3); //-X.P.F<X.P.A<X.P.A?>?>..ctor(X.P.A<X.P.A?>?)

        A.B<A> temp4 = new A.B<A>(); //-X.P.A.B<X.P.A>..ctor()
        new F<global::X.P.A.B<_>>(temp4); //-X.P.F<X.P.A.B<X.P.A>>..ctor(X.P.A.B<X.P.A>)
        new F<A.B<_>>(temp4); //-X.P.F<X.P.A.B<X.P.A>>..ctor(X.P.A.B<X.P.A>)

        A[] temp5 = new A[1];
        new F<_[]>(temp5); //-X.P.F<X.P.A[]>..ctor(X.P.A[])

        A<A>[] temp6 = new A<A>[1];
        new F<A<_>[]>(temp6); //-X.P.F<X.P.A<X.P.A>[]>..ctor(X.P.A<X.P.A>[])

        var temp7 = (1, 1);
        new F<(_, _)>(temp7); //-X.P.F<(int, int)>..ctor((int, int))

        new A<_>.F<_>(temp1); //-X.P.A<T1>.F<X.P.A>..ctor(X.P.A)
        new _(); // Error
        var temp8 = new Del<_>(Foo); //Error
    }

    class F<T> { public F(T p) {} }
    class A 
    {
        public class B<T> {}
    }
    class A<T1> 
    {
        public class F<T2>{ public F(T2 p1) {} }
    }

    delegate T Del<T>(T p1);
    int Foo(int p) {return p;}
}
""",
        Symbols.ObjectCreation,
        ImmutableArray.Create(
                // (32,15): error CS0246: The type or namespace name '_' could not be found (are you missing a using directive or an assembly reference?)
                //         new A<_>.F<_>(temp1); // Error
                Diagnostic(ErrorCode.ERR_SingleTypeNameNotFound, "_").WithArguments("_").WithLocation(32, 15),
                // (33,13): error CS0246: The type or namespace name '_' could not be found (are you missing a using directive or an assembly reference?)
                //         new _(); // Error
                Diagnostic(ErrorCode.ERR_SingleTypeNameNotFound, "_").WithArguments("_").WithLocation(33, 13),
                // (34,21): error CS0123: No overload for 'Foo' matches delegate 'P.Del<_>'
                //         var temp8 = new Del<_>(Foo); //Error
                Diagnostic(ErrorCode.ERR_MethDelegateMismatch, "new Del<_>(Foo)").WithArguments("Foo", "X.P.Del<_>").WithLocation(34, 21),
                // (34,29): error CS0246: The type or namespace name '_' could not be found (are you missing a using directive or an assembly reference?)
                //         var temp8 = new Del<_>(Foo); //Error
                Diagnostic(ErrorCode.ERR_SingleTypeNameNotFound, "_").WithArguments("_").WithLocation(34, 29)
            )
    );
        }

        [Fact]
        public void PartialConstructorTypeInference_Simple()
        {
            TestCallSites("""
using System;
using System.Collections.Generic;
namespace X;

public class P
{
    public void M1() 
    {
        new F1<_, string>(1); //-X.P.F1<int, string>..ctor(int)
        new F2<_,_>(1,""); //-X.P.F2<int, string>..ctor(int, string)
        new F3<int, _, string, _>( //-X.P.F3<int, string, string, string>..ctor(X.P.G2<string, string>)
            new G2<string, string>()  //-X.P.G2<string, string>..ctor()
        );
        new F4<_, _, string>(x => x + 1, y => y.ToString(),z => z.Length); //-X.P.F4<int, int, string>..ctor(System.Func<int, int>, System.Func<int, string>, System.Func<string, int>)
        new F5<string>(1); //-X.P.F5<string>..ctor(int, params string[])
        new F5<_>(1, ""); //-X.P.F5<string>..ctor(int, params string[])
        new F5<_>(1, "", ""); //-X.P.F5<string>..ctor(int, params string[])
        
        
        F6(
            new F1<_, string>(1) //-X.P.F1<int, string>..ctor(int)
        );
        new F7( //-X.P.F7..ctor(object)
            new F1<_, string>(1) //-X.P.F1<int, string>..ctor(int)
        );
        new F8<_>( //-X.P.F8<int>..ctor(int, object)
            1, 
            new F1<_, string>(1) //-X.P.F1<int, string>..ctor(int)
        );
        object temp1 = new F1<_, string>(1); //-X.P.F1<int, string>..ctor(int)
        temp1 = new F1<_, string>(1); //-X.P.F1<int, string>..ctor(int)
        var temp2 = new object[] { 
            new F1<_, string>(1) //-X.P.F1<int, string>..ctor(int)
        };
        new F9 { //-X.P.F9..ctor()
            P1 = new F1<_, string>(1) //-X.P.F1<int, string>..ctor(int)
        };
        new List<object> {  //-System.Collections.Generic.List<object>..ctor()
            new F1<_, string>(1) //-X.P.F1<int, string>..ctor(int)
        };
        Func<object> temp3 = () => new F1<_, string>(1); //-X.P.F1<int, string>..ctor(int)
    }

    public object M2() => new F1<_, string>(1); //-X.P.F1<int, string>..ctor(int)
    public object M3 = new F1<_, string>(1); //-X.P.F1<int, string>..ctor(int)

    class F1<T1, T2>{ public F1(T1 p1){} }
    class F2<T1, T2>{ public F2(T1 p1, T2 p2) {} }
    class F2<T1>{ public F2(T1 p1, string p2) {} }
    class F3<T1, T2, T3, T4>{ public F3(G2<T2, T4> p24) {} }
    class G2<T1, T2> {}
    class F4<T1, T2, T3>{ public F4(Func<T1, T2> p12, Func<T2, T3> p23, Func<T3, T1> p31) { } }
    class F5<T>{ public F5(int p1, params T[] args) {} }
    public void F6(object p1) {}
    class F7 
    {
        public F7(object p1) {}
    }
    class F8<T>
    { 
        public F8(T p1, object p2) {}
    }
    class F9 
    {
        public object P1 = null;
    }
}
""",
        Symbols.ObjectCreation
    );
        }

        [Fact]
        public void PartialConstructorTypeInference_Nested()
        {
            TestCallSites("""
class P
{
    void M1() 
    {
        B1<int> temp1 = null;
        new F6<A1<_>>(temp1); //-P.F6<P.A1<int>>..ctor(P.A1<int>)

        B2<int, string> temp2 = null;
        new F6<A2<_, string>>(temp2); //-P.F6<P.A2<int, string>>..ctor(P.A2<int, string>)

        C2<int, B> temp3 = null;
        new F6<I2<_, A>>(temp3); //-P.F6<P.I2<int, P.A>>..ctor(P.I2<int, P.A>)
    }   

    class F6<T1>
    { 
        public F6(T1 p1) {}
    }

    class A {}
    class B : A{}
    class A1<T> {}
    class B1<T> : A1<T> {}
    class A2<T1, T2> {}
    class B2<T1, T2> : A2<T1, T2> {}
    interface I2<in T1, out T2> {}
    class C2<T1, T2> : I2<T1, T2> {}
}      
""",
        Symbols.ObjectCreation
    );
        }

        [Fact]
        public void PartialConstructorTypeInference_Target()
        {
            TestCallSites("""
using System;
using System.Collections.Generic;

class P 
{
    void Test_VariableDeclaration() 
    {
        C1<int> temp1 = new C2<_>(); //-P.C2<int>..ctor()
    }
    
    void Test_ClassObjectCreation()
    {
        new C4( //-P.C4..ctor(P.C1<int>)
            new C2<_>() //-P.C2<int>..ctor()
        ); 

        new C5<_>( //-P.C5<P.C5<int>>..ctor(P.C5<int>)
            new C5<_>(1) //-P.C5<int>..ctor(int)
        );

       
        new C3<_>( //-P.C3<int>..ctor(P.C1<int>, int)
            new C2<_>(),  //-P.C2<int>..ctor()
            1
        );
    }

    class C3<T>
    {
        public C3(C1<T> p1, T p2) {}
    }
    class C4 
    {
        public C4(C1<int> p1) {}
    }

    void Test_InvocationExpression()
    {
        F2(new C2<_>()); //-P.C2<int>..ctor()

        F3(new C5<_>(1)); //-P.C5<int>..ctor(int)

        F1(new C2<_>(), 1);  //-P.C2<int>..ctor()
    }

    class C5<T> : C1<T>
    {
        public C5(T p1) {}
    }

    void F1<T>(C1<T> p1, T p2) {}
    void F2(C1<int> p1) {}
    void F3<T>(T p1) {}

    void Test_ArrayInitializer()
    {
        var temp1 = new C1<int>[] { 
            new C2<_>() //-P.C2<int>..ctor()
        };

        var temp2 = new [] { 
            new C1<int>(), //-P.C1<int>..ctor()
            new C2<_>() //Error: Can't be inferred
            };
    }

    void Test_ObjectInitializer()
    {
        new M4 { //-P.M4..ctor()
            P1 = new C2<_>() //-P.C2<int>..ctor()
        };
    }

    class M4 
    {
        public C1<int> P1 = null;
    }

    void Test_CollectionInitializer()
    {
        new List<C1<int>> { //-System.Collections.Generic.List<P.C1<int>>..ctor()
            new C2<_>() //-P.C2<int>..ctor()
        };
    }

    void Test_Lambda()
    {
        Func<C1<int>> temp2 = () => new C2<_>(); //-P.C2<int>..ctor()
    }
    
    void Test_Assignment() 
    {
        C1<int> temp3 = null;
        temp3 = new C2<_>(); //-P.C2<int>..ctor()
    }
    
    
    C1<int> Test_Return1() {
        return new C2<_>(); //-P.C2<int>..ctor()
    }
    
    C1<int> Test_Return2() => new C2<_>(); //-P.C2<int>..ctor()
    
    C1<int> Test_FieldInitializer = new C2<_>(); //-P.C2<int>..ctor()

    class C1<T> {}
    class C2<T> : C1<T> {}
}
""",
        Symbols.ObjectCreation,
        ImmutableArray.Create(
                // (63,17): error CS0411: The type arguments for method 'P.C2<T>.C2()' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //             new C2<_>() //Error: Can't be inferred
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "C2<_>").WithArguments("P.C2<T>.C2()").WithLocation(63, 17)
            )
    );
        }

        [Fact]
        public void PartialConstructorTypeInference_Complex()
        {
            TestCallSites("""
class Program
{
    void M() 
    {
        F1(new C9<_,_,int,_>(1), 1); //-Program.C9<int, int, int, Program.C1<int>>..ctor(int)
    }

    void F1<T>(C1<int> p1, T p2) {}

    class C1<T> {}

    class C9<T1, T2, T3, T4> : C1<T2> where T4 : C1<T3>
    {
        public C9(T1 p1) {}
    }
}
""",
        Symbols.ObjectCreation
    );
        }

        [Fact]
        public void PartialConstructorTypeInference_Constraints()
        {
            TestCallSites("""
class Program 
{
    public void M1() 
    {
        new C4<_,_>(1); //-C4<C1<int>, int>..ctor(int)

        new C5<_,_>( //-C5<C7, C7>..ctor(C7)
            new C7() //-C7..ctor()
        ); 
    }
}

class C1<T> {}
class C4<T1, T2> where T1 : C1<T2> 
{
    public C4(T2 p1) {}
}

public class C5<T1, T2> 
    where T1 : C6<T2>
    where T2 : C6<T1>
{
    public C5(T1 p1) {}
}

public class C6<T> {}
public class C7 : C6<C7> {}
""",
        Symbols.ObjectCreation
    );
        }

        [Fact]
        public void PartialConstructorTypeInference_ErrorRecovery()
        {
            TestCallSites("""
class P {
    void M1() 
    {
        new F1<_,_>(""); // Error: Can't infer T2
        new F1<int, string>(""); // Error: int != string
        new F1<byte,_>(257); // Error: Can't infer T2    
        F2(new B()); //Error
        F2(new F1<_,_>("")); // Error
        F3(new F1<_,_>(1)); // Error
        new F1<_,_>(new F1<_,_>(1)); // Error
    }
    class F1<T1, T2>{ public F1(T1 p1) {} }
    void F2<T1>(T1 p1) {}
    void F3<T1, T2>(T1 p1) {}
}
""",
        Symbols.ObjectCreation,
        ImmutableArray.Create(
                // (4,13): error CS0411: The type arguments for method 'P.F1<T1, T2>.F1(T1)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         new F1<_,_>(""); // Error: Can't infer T2
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1<_,_>").WithArguments("P.F1<T1, T2>.F1(T1)").WithLocation(4, 13),
                // (5,29): error CS1503: Argument 1: cannot convert from 'string' to 'int'
                //         new F1<int, string>(""); // Error: int != string
                Diagnostic(ErrorCode.ERR_BadArgType, @"""""").WithArguments("1", "string", "int").WithLocation(5, 29),
                // (6,13): error CS0411: The type arguments for method 'P.F1<T1, T2>.F1(T1)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         new F1<byte,_>(257); // Error: Can't infer T2    
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1<byte,_>").WithArguments("P.F1<T1, T2>.F1(T1)").WithLocation(6, 13),
                // (7,16): error CS0246: The type or namespace name 'B' could not be found (are you missing a using directive or an assembly reference?)
                //         F2(new B()); //Error
                Diagnostic(ErrorCode.ERR_SingleTypeNameNotFound, "B").WithArguments("B").WithLocation(7, 16),
                // (8,16): error CS0411: The type arguments for method 'P.F1<T1, T2>.F1(T1)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F2(new F1<_,_>("")); // Error
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1<_,_>").WithArguments("P.F1<T1, T2>.F1(T1)").WithLocation(8, 16),
                // (9,16): error CS0411: The type arguments for method 'P.F1<T1, T2>.F1(T1)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F3(new F1<_,_>(1)); // Error
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1<_,_>").WithArguments("P.F1<T1, T2>.F1(T1)").WithLocation(9, 16),
                // (10,13): error CS0411: The type arguments for method 'P.F1<T1, T2>.F1(T1)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         new F1<_,_>(new F1<_,_>(1)); // Error
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1<_,_>").WithArguments("P.F1<T1, T2>.F1(T1)").WithLocation(10, 13)
        )
    );
        }

        [Fact]
        public void PartialConstructorTypeInference_Dynamic()
        {
            TestCallSites("""
class P {
    void M1() 
    {
        dynamic temp4 = "";
            
        // Inferred: [T1 = int] Error: T1 = string & int
        new F7<string, _>("", temp4, 1);
                    
        // Inferred: [T1 = int] Error: Inferred type argument is not supported by runtime (type hints will not be used at all)
        new F7<_, string>(1, temp4, 1);
    }
            
    class F7<T1, T2>{ public F7(T1 p1, T2 p2, T1 p3) {} }
}
""",
        Symbols.ObjectCreation,
        ImmutableArray.Create(
                // (7,9): error CS9215: Can't use inferred type arguments in object creation containing dynamic arguments.
                //         new F7<string, _>("", temp4, 1);
                Diagnostic(ErrorCode.ERR_TypeHintsInDynamicObjectCreation, @"new F7<string, _>("""", temp4, 1)").WithLocation(7, 9),
                // (10,9): error CS9215: Can't use inferred type arguments in object creation containing dynamic arguments.
                //         new F7<_, string>(1, temp4, 1);
                Diagnostic(ErrorCode.ERR_TypeHintsInDynamicObjectCreation, "new F7<_, string>(1, temp4, 1)").WithLocation(10, 9))
        );
        }

        [Fact]
        public void PartialConstructorTypeInference_Nullable()
        {
            TestCallSites("""
#nullable enable
class P
{
    void M() 
    {
        string temp0 = "";
        new C1<_?>(temp0); //-P.C1<string?>..ctor(string?)
        
        new C1<_>(temp0); //-P.C1<string>..ctor(string)

        string? temp1 = null;
        new C1<_?>(temp1); //-P.C1<string?>..ctor(string?)
        
        new C1<_>(temp1); //-P.C1<string?>..ctor(string?)
        
        F1(new C2<_?>(), temp1); //-P.C2<string?>..ctor()

        F1(new C2<_>(), temp1); //-P.C2<string?>..ctor()
    }

    void F1<T>(C2<string?> p1, T p2) {}

    class C2<T> 
    {
        public C2() {}
    }

    class C1<T>
    {
        public C1(T p1) {}
    }
}
""",
        Symbols.ObjectCreation
    );
        }
        #endregion
    }
}
