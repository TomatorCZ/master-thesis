# Type inference when implementing multiple same generic interfaces

## Summary

Improve inference algorithm to infer type arguments when there is an argument implementing multiple same interfaces.

## Motivation

Consider the following scenario.

```csharp
interface I<T> {}
class X<T> {}
class Y {}
class Z {}  
class C : I<X<Y>>, I<Z> {}
public class P
{   
    static void M<T>(I<X<T>> i) { }
    public static void Main()
    {
		//Error
        M(new C());
    }
}
```

## Detailed design

>TBF

## Required changes

1. Inference algorithm

>TBF