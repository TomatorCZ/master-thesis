# Inference based on target

## My notes

> Source:
> [csharplang/discussions#1349](https://github.com/dotnet/csharplang/issues/1349),
> [roslyn/issues#5429](https://github.com/dotnet/roslyn/issues/5429),
> [csharplang/discussions#92](https://github.com/dotnet/csharplang/discussions/92),
> [csharplang/discussions#4527](https://github.com/dotnet/csharplang/discussions/4527),
> [csharplang/issues#2701](https://github.com/dotnet/csharplang/issues/2701),
> [csharplang/discussions#450](https://github.com/dotnet/csharplang/discussions/450)

**Idea**

Allowing to infer type from method target.

**Examples**

Introduced by [csharplang/discussions#1349](https://github.com/dotnet/csharplang/issues/1349).

```c#
public T Field<T>(string name){}
...
int id = row.Field("id");
```

```c#
IEnumerable<KeyValuePair<string, string>> Headers = new[]
{
     new("Foo", foo),
     new("Bar", bar),
}
```

## Proposal