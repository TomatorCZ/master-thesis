# Improving delegate overload resolution

## My notes

> Source: 
> [csharplang/issues#3277](https://github.com/dotnet/csharplang/issues/3277)

**Idea** 

Improve overload resolution when looking for applicable methods by removing methods that cannot be compatible.

**Example**

```c#
public class Program1
{
    delegate void MyAction<T>(T x);

    void Y(long x) { }

    void D(MyAction<int> o) { }
    void D(MyAction<long> o) { }

    void T()
    {
        D(Y); // Ambiguous between both D calls, despite the fact that `void D(MyAction<int>)` is not a valid target.
    }
}
```

## Proposal