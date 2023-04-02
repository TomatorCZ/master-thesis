# Type inference of constructor

## Summary

Currently, C# allows inferring the whole type during its creation by `new()` construct based on the target.
Although it's very useful, it disallows to write generic type without type arguments and lets the compiler infer its type arguments.
We propose to use the method inference to determine the type arguments of created type.

## Motivation

There are situations, where we have enough sources of information(target, used arguments) to determine all type arguments of some type. 
We have just to pick the implementation.
When we use custom helper methods for object creation like `Create` or etc., we can use the advantage of method inference and avoid specifying type arguments.
In the case of a constructor, it is prohibited.
We feel it could be better.

```csharp
//Error
object foo1 = new Foo(1,1);
//Ok
object foo2 = Helper.Create(1,1);

static class Helper {
    public static Foo<T1, T2> Create<T1, T2>(T1 p1, T2 p2) {
        return new (p1,p2);
    }
}

class Foo<T1, T2> {
    public Foo(T1 p1, T2 p2){}
}
```

One can argue to use already existing inference using target and replace `object foo1` with `Foo<int, int>`. 
In that case, we could use only `new()` construct.
However, this solution is possible only in specific situations, where we know the target and instance at the same time.
A common situation is different.
The target is usually a general structure, which gives us only an interface.
In these situations, we are not able to use the target in inference and we have to write type arguments even if there are already in the code.

Since we investigate the constructors, we could cover also objects and list initializers.
However, we postpone it to future improvement of generic resolution based on later calls since the initializers are lowered into multiple calls of special statements.
 
## Detailed design

> TBF



