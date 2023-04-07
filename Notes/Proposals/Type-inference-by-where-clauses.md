# Type inference by where clauses

## Summary

Improve type inference by employing `where` clauses.

## Motivation

C# type inference doesn't use information from `where` clauses which would lead to better inference improvement.
See the following example.

```csharp
public static Foo<TBar, T> AsFoo(this TBar bar) where TBar : IBar<T>
{
     return new Foo<TBar, T>(bar);
}

var foo = bar.AsFoo(); // type parameters can not be inferred.
```

We propose a change of type inference taking the `where` clauses into account.

## Detailed design

> Algorithm

Unfortunately, the improvement introduces a breaking change.
See the following example.

```csharp
void M(object) {}
void M<T, U>(T t) where T : IEnumerable<U> {}
...
M("foo");
```

However, we think that the advantage of this improvement is big and suggest an analyzer, which will detect these problematic parts of code in the codebase and suggest a patch preserving old behavior of the program.


### Type inference improvment

> TODO

### Analysis of breaking change

> TODO

## Required changes