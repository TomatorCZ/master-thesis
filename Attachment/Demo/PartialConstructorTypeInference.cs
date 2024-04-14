static class PartialConstructorTypeInference 
{
    #region Example1
    public static void RunExample1()
    {
        Console.WriteLine(nameof(RunExample1));

        new C1<_>(1);
    }
    class C1<T1> 
    {
        public C1(T1 p1) {
            Console.WriteLine($"{nameof(T1)} = {typeof(T1).FullName}");
        }
    }
    #endregion

    #region Example2
    public static void RunExample2() 
    {
        Console.WriteLine(nameof(RunExample2));

        new C2<IList<_>>(new List<int>());
    }

    class C2<T1> 
    {
        public C2(T1 p1) {
             Console.WriteLine($"{nameof(T1)} = {typeof(T1).FullName}");
        }
    }
    #endregion

    #region Example3
    public static void RunExample3() 
    {
        Console.WriteLine(nameof(RunExample3));

        C3<int> a = new C3<_>();
    }

    class C3<T1>
    {
        public C3() {
            Console.WriteLine($"{nameof(T1)} = {typeof(T1).FullName}");
        }
    }
    #endregion

    #region Example4
    public static void RunExample4() 
    {
        Console.WriteLine(nameof(RunExample4));

        new C4<_, int>();
    }

    class C4<T1, T2> where T1 : List<T2>
    {
        public C4() {
            Console.WriteLine($"{nameof(T1)} = {typeof(T1).FullName}");
            Console.WriteLine($"{nameof(T2)} = {typeof(T2).FullName}");
        }
    }
    #endregion
}
