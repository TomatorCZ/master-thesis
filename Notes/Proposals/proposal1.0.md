# Constrained type inference

## My notes

> Source:
> [csharplang/discussion#6930](https://github.com/dotnet/csharplang/discussions/6930,
> [comment](https://github.com/dotnet/roslyn/pull/7850#issuecomment-170154270),
> [csharplang/discussions#478](https://github.com/dotnet/csharplang/discussions/478),
> [csharplang/discussions#741](https://github.com/dotnet/csharplang/discussions/741),
> [csharplang/discussions#997](https://github.com/dotnet/csharplang/discussions/997),
> [roslyn/issues#502](https://github.com/dotnet/roslyn/issues/5023),
> [roslyn/issues#15166](https://github.com/dotnet/roslyn/issues/15166),
> [csharplang/discussions#1018](https://github.com/dotnet/csharplang/discussions/1018),
> [csharplang/discussions#289](https://github.com/dotnet/csharplang/discussions/289),
> [csharplang/discussions#2845](https://github.com/dotnet/csharplang/discussions/2845),
> [csharplang/discussions#1103](https://github.com/dotnet/csharplang/discussions/1103),
> [csharplang/discussions#5430](https://github.com/dotnet/csharplang/discussions/5430),
> [csharplang/discussions#5173](https://github.com/dotnet/csharplang/discussions/5173),
> [csharplang/issues#1324](https://github.com/dotnet/csharplang/issues/1324)

**Idea**

Let compiler using constraints to infer other arguments, or annotating it ([csharplang/discussions#1103](https://github.com/dotnet/csharplang/discussions/1103)).

**Examples**

```c#
class A<T1,T2> where T1 : List<T2> {}
...
var a = new A<List<int>,>() // T2 is int
```

**Issues**

It has to be used with *Using char as inferred type argument* because of breaking change. See [comment](https://github.com/dotnet/roslyn/pull/7850#issuecomment-170154270)

## Proposal