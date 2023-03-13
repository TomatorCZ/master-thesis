# Type inference by method group

## My notes

> Source: 
> [csharplang/discussions#129](https://github.com/dotnet/csharplang/discussions/129),
> [csharplang/discussions#5963](https://github.com/dotnet/csharplang/discussions/5963),
> [csharplang/discussions#3722](https://github.com/dotnet/csharplang/discussions/3722),
> [csharplang/meetings](https://github.com/dotnet/csharplang/blob/main/meetings/2021/LDM-2021-04-21.md#inferred-types-for-lambdas-and-method-groups)

**Idea**

Allow infer type from method group of size 1.

**Examples**

```c#
static bool IsEven(int x) => x % 2 == 0;

static void Test<T>(Func<T, bool> predicate) {}

Test(IsEven);
```

## Proposal