# Type inference of ref/out params in lambdas

## Summary

Let the compiler infer parameter types of lambda expressions when they contain out/ref parameters.

## Motivation

C# doesn't infer parameter types of lambda expressions when there contains ref/out keywords.
Since it doesn't influence type inference, let's leave it to the compiler.

```csharp
delegate bool TryParse<T>(string text, out T result);

//Ok
TryParse<int> parse1 = (string text, out int result) => Int32.TryParse(text, out result);

//Error
TryParse<int> parse2 = (text, out result) => Int32.TryParse(text, out result);
```

## Detailed design

> TBF