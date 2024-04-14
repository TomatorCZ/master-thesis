static class PartialMethodTypeInference 
{
    #region Example1
    public static void RunExample1() 
    {
        Console.WriteLine(nameof(RunExample1));

        M1<_, string>(1);
    }

    private static void M1<T1, T2>(T1 p1) 
    {
        Console.WriteLine($"{nameof(T1)} = {typeof(T1).FullName}");
        Console.WriteLine($"{nameof(T2)} = {typeof(T2).FullName}");
    }
    #endregion

    #region Example2
    public static void RunExample2() 
    {
        Console.WriteLine(nameof(RunExample2));

        M2<IList<_>>(new List<int>());
    }

    private static void M2<T1>(T1 p1) 
    {
        Console.WriteLine($"{nameof(T1)} = {typeof(T1).FullName}");
    }
    #endregion
}
