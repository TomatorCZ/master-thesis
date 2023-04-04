# Method return type inference

## Summary

Allow user to omit return type in local functions and private methods made from expressions. 

## Motivation

From the conceptual view of the signatures, C# method signatures are designed to define a contract used by other code.
Although it's a good idea, it restrics on all method signatures including private and local functions.
These functions doesn't expose API to be used outside the context allowing us to infer the return type from function body.

## Detailed design

However these analysis is difficult in general, we can restrict the inference only on simple cases.
The simple cases are functions having expression as a method body.

> Example

```csharp
class Program {
    public void Main() {
        Console.WriteLine(helper());
        Console.WriteLine(foo());

        var helper() => 1;
    }

    private var foo() => 1;
}
```

We have to be aware of self-referencing functions prohibiting inference.

## Required changes

It needs to change Roslyn pipeline of binding C# code.