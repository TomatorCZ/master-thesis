# Improve infrence of type deconstruction

## My notes

> Source:
> [csharplang/discussions#3621](https://github.com/dotnet/csharplang/discussions/3621)

**Idea**

Choose correct overload based on target type

**Examples**

```c#
public static void Deconstruct(this Person p, out string name, out int age) =>
    (name, age) = (p.Name, p.Age);

public static void Deconstruct(this Person p, out string name, out string email) =>
    (name, email) = (p.Name, p.Email);

(string name, string email) = new Person(...);
```

## Proposal