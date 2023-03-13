# Existential types

## My notes

> Source:
> [csharplang/issues#1328](https://github.com/dotnet/csharplang/issues/1328)
> [csharplang/issues#5556](https://github.com/dotnet/csharplang/issues/5556)

**Idea**

Hiding type arguments of class to user, which doesn't care about the type

**Examples**

```c#
interface ICounter<protected T>
{
    T Start { get; }
    void Next(T current);
    bool Done { get; }
}
...
void M(ICounter ic)
{
    var x = ic.Start;
    while (!ic.Done)
    {
        x = ic.Next(x);
    }
}
```

## Proposal