# Inference based on later methods call


rust!!!

object initilizer
collection initilizer
etc...


The next improvement regards object initilizers.
Type deduction from object initilizers shown to be useful in Rust.
We could do the same thing in C#.

```csharp
struct Foo<T1, T2> {
    public T1 p1;
    public T2 p2;
}
...
//Ok
var temp1 = new Foo<int, int>{p1=1, p2=2};
//Error
var temp2 = new Foo{p1 = 1, p2 = 2};
```

In the end, we want to focus on collection initilizers.
As we said in at the begenning, We would like to let compiler infer type arguments of constructed type.
When we focus on collection initilizers, it would look like this
We can inspire by rust  

```csharp
class C<T> {
    void Add
}
```

## My notes

> Source:
> [csharplang/issues#1349](https://github.com/dotnet/csharplang/issues/1349),
> [roslyn/issues#8214](https://github.com/dotnet/csharplang/issues/253)
> [rust](https://doc.rust-lang.org/rust-by-example/types/inference.html)

**Idea**

Let compiler decides actual type of variable based on further calls.

**Examples**

```c#
var a = List<_>()
a.add(1); // a = List<int>
```

## Proposal