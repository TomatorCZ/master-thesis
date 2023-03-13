# Inference based on later methods call

## My notes

> Source:
> [csharplang/issues#1349](https://github.com/dotnet/csharplang/issues/1349),
> [roslyn/issues#8214](https://github.com/dotnet/csharplang/issues/253)
> [rust](https://doc.rust-lang.org/rust-by-example/types/inference.html)

**Idea**

Let compiler decides actual type of variable based on further calls.

**Examples**

```c#
var a = List<_>()
a.add(1); // a = List<int>
```

## Proposal