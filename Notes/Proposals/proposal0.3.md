# Constructor type inference

## My notes

> Source:
> [csharplang/discussions#1349](https://github.com/dotnet/csharplang/issues/1349),
> [csharplang/discussions#1162](https://github.com/dotnet/csharplang/discussions/1162),
> [csharplang/discussions#5211](https://github.com/dotnet/csharplang/discussions/5211),
> [csharplang/discussions#281](https://github.com/dotnet/csharplang/discussions/281),
> [csharplang/discussions#2935](https://github.com/dotnet/csharplang/discussions/2935),
> [csharplang/discussions#427](https://github.com/dotnet/csharplang/discussions/427),
> [roslyn/issues#2319](https://github.com/dotnet/roslyn/issues/2319)

**Idea**

Let compiler decide type parameters based on parameters or initialization list.

**Examples**

```c#
    var arrayOfNumbers = new[] { 1, 2, 3, 4 };
    var g = new List(arrayOfNumbers);
```

## Proposal