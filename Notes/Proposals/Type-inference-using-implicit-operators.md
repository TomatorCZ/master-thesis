# Type inference using implicit operators

## Summary

Allow compiler to use operators for implicit casting during inference.

# Motivation

We noticed the following behavior.

```csharp
using System;

//Ok
Wrapper<int> temp = 1;
//Error
Consume(1);

void Consume<T>(Wrapper<T> wrapper) { /* some code */ }

class Wrapper<T>
{
    public T Value { get; }
    public Wrapper(T value)
    {
        this.Value = value;
    }

    public static implicit operator Wrapper<T>(T value)
        => new Wrapper<T>(value);
}
```

We would expect if the compiler doesn't need our advice to convert the `int` to `Wrapper<int>` in the former example, the it uses the same mechanism for inference.

## Detailed design

> TBF