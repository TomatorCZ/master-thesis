# Using char to determine inferred type argument

## My notes

> Source:
> [csharplang/discussions#1348](https://github.com/dotnet/csharplang/discussions/1348),
> [csharplang/discussions#1349](https://github.com/dotnet/csharplang/issues/1349),
> [csharplang/discussions#6479](https://github.com/dotnet/csharplang/discussions/6479),
> [stackoverflow](https://stackoverflow.com/questions/53683564/fluent-interface-for-generic-type-hierarchy)

**Idea**

The goal is to specify which type argument should be inferred by the compiler.

**Examples**

Introduced by [csharplang/discussions#1348](https://github.com/dotnet/csharplang/discussions/1348).

```c#
void Foo<T1, TResult>(T1 t1){}
...
Foo<var, int>(0);
```

More complex variant

```C#
Foo<List<_>, int>(null);
```

## Proposal