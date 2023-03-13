# Named typed arguments

## My notes

> Source:
> [csharplang/discussions#280](https://github.com/dotnet/csharplang/discussions/280),
> [csharplang/discussions#279](https://github.com/dotnet/csharplang/discussions/279)

**Idea**

Allowing to specify type of type argument based on its name. It could be combined with *Default type paramenters* and further type inference.

**Examples**

```c#
U F<T, U>(T t) { .. }

var x = F<U:short>(1); // F<int, short>
```

## Proposal