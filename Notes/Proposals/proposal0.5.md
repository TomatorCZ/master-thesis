# Type Inference of ref/out params in lambdas

## My notes

> Source:
> [csharplang/issues#338](https://github.com/dotnet/csharplang/issues/338)

**Idea**

Allow to deduce parameter types of lambda from delegate.

**Examples**

```c#
delegate bool TryParse<T>(string text, out T result);
TryParse<int> parse2 = (text, out result) => Int32.TryParse(text, out result);
```

## Proposal