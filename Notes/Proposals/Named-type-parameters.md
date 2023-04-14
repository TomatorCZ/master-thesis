# Named type parameters

## Detailed design

We will take an example from named method parameters again and introduce them in type parameters.

```csharp
class Foo<T1, T2> {}

class Bar : Foo<T2:int, T1:string> {}
```

**Lowering**

We have to lower named type parameters to be able to compile the into *CIL*, which doesn't know them.
It can be done by reordering them to accordingly match positional parameters.
The same trick is used for named method type parameters.