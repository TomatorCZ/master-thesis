# Target-typed inference for switch expression and deconstruction

## My notes

> Source:
> [csharplang/discussions#2898](https://github.com/dotnet/csharplang/discussions/2898)

**Idea**

Let compiler to deduce type from using tuples in switch assignment.

**Examples**

```c#
(type, getValue) = info switch
{
    PropertyInfo pi => (pi.PropertyType, () => pi.GetValue(d)),
    FieldInfo fi => (fi.FieldType, () => fi.GetValue(d))
};
```

## Proposal