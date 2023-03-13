# Aliases defining partial type arguments

## My notes

> Source:
> [csharplang/issues#1239](https://github.com/dotnet/csharplang/issues/1239),
> [csharplang/issue#4284](https://github.com/dotnet/csharplang/issues/4284)

**Idea**

Allow user using aliases to define some type arguments.

**Examples**

```c#
using MyList<T> = System.Collections.Generic.List<T>;
using StringDictionary<TValue> = System.Collections.Generic.Dictionary<String, TValue>;
```

## Proposal