# C# tasks

## Type inference

### Partial type inference

There is a couple of tasks regarding partial type inference.

#### Default type paramenters

> Source:
> [csharplang/discussion#278](https://github.com/dotnet/csharplang/discussions/278),
> [roslyn/issues#6248](https://github.com/dotnet/roslyn/issues/6248),
> [csharplang/discussions#4035](https://github.com/dotnet/csharplang/discussions/4035)

**Idea**

Allowing to define predefined type arguments to not borther with specifying it in usage. Can be combined well with skipping type args list.

**Examples**

Basic usage can look like this. ([csharplang/discussion#278](https://github.com/dotnet/csharplang/discussions/278))

```c#
class X<T = object> {}
...
var x = new X();
```

Further improvments can evolve `this` as default. ([roslyn/issues#6248](https://github.com/dotnet/roslyn/issues/6248))

```c#
interface IEquitable<T = this> {}

class X : IEquitable {} // IEquitable<X>
```

Also, the default type parameters could be resolved be default parameters. ([csharplang/discussions#4035](https://github.com/dotnet/csharplang/discussions/4035))
```c#
public static void Bar<A>(A a=42) {}
...
Bar();
```

It could have syntax in style of attributes. ([csharplang/discussion#278](https://github.com/dotnet/csharplang/discussions/278))

```c#
class X<[DefaultGenericArgument(typeof(C))] T> where T : C
```

#### Aliases defining partial type arguments

> Source:
> [csharplang/issues#1239](https://github.com/dotnet/csharplang/issues/1239),
> [csharplang/issue#4284](https://github.com/dotnet/csharplang/issues/4284)

**Idea**

Allow user using aliases to define some type arguments.

**Examples**

```c#
using MyList<T> = System.Collections.Generic.List<T>;
using StringDictionary<TValue> = System.Collections.Generic.Dictionary<String, TValue>;
```

#### Named typed arguments

> Source:
> [csharplang/discussions#280](https://github.com/dotnet/csharplang/discussions/280),
> [csharplang/discussions#279](https://github.com/dotnet/csharplang/discussions/279)

**Idea**

Allowing to specify type of type argument based on its name. It could be combined with *Default type paramenters* and further type inference.

**Examples**

```c#
U F<T, U>(T t) { .. }

var x = F<U:short>(1); // F<int, short>
```

#### Using char as inferred type argument

> Source:
> [csharplang/discussions#1348](https://github.com/dotnet/csharplang/discussions/1348),
> [csharplang/discussions#1349](https://github.com/dotnet/csharplang/issues/1349),
> [csharplang/discussions#6479](https://github.com/dotnet/csharplang/discussions/6479),
> [stackoverflow](https://stackoverflow.com/questions/53683564/fluent-interface-for-generic-type-hierarchy)

**Idea**

The goal is to specify which type argument should be inferred by the compiler.

**Examples**

Introduced by [csharplang/discussions#1348](https://github.com/dotnet/csharplang/discussions/1348).

```c#
void Foo<T1, TResult>(T1 t1){}
...
Foo<var, int>(0);
```

More complex variant

```C#
Foo<List<_>, int>(null);
```

#### Inference based on target

> Source:
> [csharplang/discussions#1349](https://github.com/dotnet/csharplang/issues/1349),
> [roslyn/issues#5429](https://github.com/dotnet/roslyn/issues/5429),
> [csharplang/discussions#92](https://github.com/dotnet/csharplang/discussions/92),
> [csharplang/discussions#4527](https://github.com/dotnet/csharplang/discussions/4527),
> [csharplang/issues#2701](https://github.com/dotnet/csharplang/issues/2701),
> [csharplang/discussions#450](https://github.com/dotnet/csharplang/discussions/450)

**Idea**

Allowing to infer type from method target.

**Examples**

Introduced by [csharplang/discussions#1349](https://github.com/dotnet/csharplang/issues/1349).

```c#
public T Field<T>(string name){}
...
int id = row.Field("id");
```

```c#
IEnumerable<KeyValuePair<string, string>> Headers = new[]
{
     new("Foo", foo),
     new("Bar", bar),
}
```

#### Constrained type inference

> Source:
> [csharplang/discussion#6930](https://github.com/dotnet/csharplang/discussions/6930,
> [comment](https://github.com/dotnet/roslyn/pull/7850#issuecomment-170154270),
> [csharplang/discussions#478](https://github.com/dotnet/csharplang/discussions/478),
> [csharplang/discussions#741](https://github.com/dotnet/csharplang/discussions/741),
> [csharplang/discussions#997](https://github.com/dotnet/csharplang/discussions/997),
> [roslyn/issues#502](https://github.com/dotnet/roslyn/issues/5023),
> [roslyn/issues#15166](https://github.com/dotnet/roslyn/issues/15166),
> [csharplang/discussions#1018](https://github.com/dotnet/csharplang/discussions/1018),
> [csharplang/discussions#289](https://github.com/dotnet/csharplang/discussions/289),
> [csharplang/discussions#2845](https://github.com/dotnet/csharplang/discussions/2845),
> [csharplang/discussions#1103](https://github.com/dotnet/csharplang/discussions/1103),
> [csharplang/discussions#5430](https://github.com/dotnet/csharplang/discussions/5430),
> [csharplang/discussions#5173](https://github.com/dotnet/csharplang/discussions/5173),
> [csharplang/issues#1324](https://github.com/dotnet/csharplang/issues/1324)

**Idea**

Let compiler using constraints to infer other arguments, or annotating it ([csharplang/discussions#1103](https://github.com/dotnet/csharplang/discussions/1103)).

**Examples**

```c#
class A<T1,T2> where T1 : List<T2> {}
...
var a = new A<List<int>,>() // T2 is int
```

**Issues**

It has to be used with *Using char as inferred type argument* because of breaking change. See [comment](https://github.com/dotnet/roslyn/pull/7850#issuecomment-170154270)

#### Type inference based on return type

> Source:
> [csharplang/discussions#6452](https://github.com/dotnet/csharplang/discussions/6452),
> [csharplang/discussions#265](https://github.com/dotnet/csharplang/discussions/265)

**Idea**

Let compiler deducing return type argument based on return type.

**Examples**

```c#
public static Add( x, y )
{
    return x + y;
}
```

```c#
public static T Add<T>( int x, int y )
{
    return x + y;
}
```

Simplified to use only local functions with expression body.

```c#
var add(int a, int b) => a+b;
```

### Other inference

#### Existencial types

> Source:
> [csharplang/issues#1328](https://github.com/dotnet/csharplang/issues/1328)
> [csharplang/issues#5556](https://github.com/dotnet/csharplang/issues/5556)

**Idea**

Hiding type arguments of class to user, which doesn't care about the type

**Examples**

```c#
interface ICounter<protected T>
{
    T Start { get; }
    void Next(T current);
    bool Done { get; }
}
...
void M(ICounter ic)
{
    var x = ic.Start;
    while (!ic.Done)
    {
        x = ic.Next(x);
    }
}
```

#### Inference based on later methods call (first usage)

> Source:
> [csharplang/issues#1349](https://github.com/dotnet/csharplang/issues/1349),
> [roslyn/issues#8214](https://github.com/dotnet/csharplang/issues/253)
> [rust](https://doc.rust-lang.org/rust-by-example/types/inference.html)
> [Hindley-Miller](https://medium.com/swlh/stretching-the-reach-of-implicitly-typed-variables-in-c-16882318a92)

**Idea**

Let compiler decides actual type of variable based on further calls.

**Examples**

```c#
var a = List<_>()
a.add(1); // a = List<int>
```

#### Specifing type arguments in method calls (Realocation) 

> Source:
> [roslyn/issues#8214](https://github.com/dotnet/roslyn/issues/8214)

**Idea**

Allowing to specify type arguments in following calls.

**Examples**

```c#
static class A<T1> {
    public static void Foo<T2>(){}
}
...
A.Foo<int;string>();
```

#### Constructor type inference

> Source:
> [csharplang/discussions#1349](https://github.com/dotnet/csharplang/issues/1349),
> [csharplang/discussions#1162](https://github.com/dotnet/csharplang/discussions/1162),
> [csharplang/discussions#5211](https://github.com/dotnet/csharplang/discussions/5211),
> [csharplang/discussions#281](https://github.com/dotnet/csharplang/discussions/281),
> [csharplang/discussions#2935](https://github.com/dotnet/csharplang/discussions/2935),
> [csharplang/discussions#427](https://github.com/dotnet/csharplang/discussions/427),
> [roslyn/issues#2319](https://github.com/dotnet/roslyn/issues/2319)
> https://github.com/dotnet/csharplang/issues/151
> https://github.com/dotnet/csharplang/blob/main/proposals/collection-literals.md

**Idea**

Let compiler decide type parameters based on parameters or initialization list.

**Examples**

```c#
    var arrayOfNumbers = new[] { 1, 2, 3, 4 };
    var g = new List(arrayOfNumbers);
```

Problematic, but would be cool

```c#
DefaultHttpContext context = new().WithAuthorizationHeader($"Bearer {token}");
```

#### Inference of generic constraints

> Source:
> [csharplang/discussions#279](https://github.com/dotnet/csharplang/discussions/279),
> [blog/ericlippert](https://ericlippert.com/2013/07/15/why-are-generic-constraints-not-inherited/),
> [csharplang/discussions#772](https://github.com/dotnet/csharplang/discussions/772)

**Idea**

Allowing to inherit constrains to avoid copying contrains 

**Examples**

Introduced in [csharplang/discussions#279](https://github.com/dotnet/csharplang/discussions/279).

```c#
public class Super<T> : Base<T> {} // inffered T : Person

public class Base<T : Person> {}
```

#### Test objects during runtime using generic wildcards

> Source:
> [csharplang/discussions#1992](https://github.com/dotnet/csharplang/discussions/1992)

**Idea**

Let user to test whatever the item is some type of generics without using type parameters.

**Examples**

```c#
if (obj is IEnumerable<?> items) 
...
if (obj is IEnumerable<?T>) 
...
if (items is List<?T> list)
...
if (control is LabelControl<?T> label where T : IDisplay) 
...
```

#### Target-typed inference for switch expression and deconstruction

> Source:
> [csharplang/discussions#2898](https://github.com/dotnet/csharplang/discussions/2898)

**Idea**

Let compiler to deduce type from using tuples in switch assignment.

**Examples**

```c#
(type, getValue) = info switch
{
    PropertyInfo pi => (pi.PropertyType, () => pi.GetValue(d)),
    FieldInfo fi => (fi.FieldType, () => fi.GetValue(d))
};
```

#### Improving delegate overload resolution

> Source: 
> [csharplang/issues#3277](https://github.com/dotnet/csharplang/issues/3277)

**Idea** 

Improve overload resolution when looking for applicable methods by removing methods that cannot be compatible.

**Example**

```c#
public class Program1
{
    delegate void MyAction<T>(T x);

    void Y(long x) { }

    void D(MyAction<int> o) { }
    void D(MyAction<long> o) { }

    void T()
    {
        D(Y); // Ambiguous between both D calls, despite the fact that `void D(MyAction<int>)` is not a valid target.
    }
}
```

#### Infer lambdas in local variable inference

> Source:
> [csharplang/discussions#1694](https://github.com/dotnet/csharplang/discussions/1694)

**Idea**

Allow to infer type of lambda despite of unequivalety of delegates.

#### Type inference by method group

> Source: 
> [csharplang/discussions#129](https://github.com/dotnet/csharplang/discussions/129),
> [csharplang/discussions#5963](https://github.com/dotnet/csharplang/discussions/5963),
> [csharplang/discussions#3722](https://github.com/dotnet/csharplang/discussions/3722),
> [csharplang/meetings](https://github.com/dotnet/csharplang/blob/main/meetings/2021/LDM-2021-04-21.md#inferred-types-for-lambdas-and-method-groups)

**Idea**

Allow infer type from method group of size 1.

**Examples**

```c#
static bool IsEven(int x) => x % 2 == 0;

static void Test<T>(Func<T, bool> predicate) {}

Test(IsEven);
```

#### ref/out params in lambdas without typename

> Source:
> [csharplang/issues#338](https://github.com/dotnet/csharplang/issues/338)

**Idea**

Allow to deduce parameter types of lambda from delegate.

**Examples**

```c#
delegate bool TryParse<T>(string text, out T result);
TryParse<int> parse2 = (text, out result) => Int32.TryParse(text, out result);
```

#### Inference using implicit operators

> Source:
> [csharplang/discussions#2067](https://github.com/dotnet/csharplang/discussions/2067)

**Idea**

Inference will take into consideration implicit operators.

**Examples**

```c#
class Wrapper<T> {
    public T Value { get; }
    public Wrapper(T value){ this.Value = value; }
    public static implicit operator Wrapper<T>(T value) => new Wrapper<T>(value);
}
void Consume<T>(Wrapper<T> wrapper) { /* some code */ }
...
int i = 0;
Consume(i);
```

#### Infer lambda parameters type and signatures based on usage

> Source:
> [csharplang/discussions#4443](https://github.com/dotnet/csharplang/discussions/4443)

**Idea**

Deduce lamda parameters types based on usage.

**Examples**

```c#
var f = (name) => name + "aa";
```

#### Improve infrence of type deconstruction

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

#### Type inference when inhereting multible single generic interface

> Source: 
> [csharplang/discussions#1592](https://github.com/dotnet/csharplang/discussions/1592)

**Idea**
Looks like the algorithm is weak.

**Examples**

```c#
interface I<T> { }
class X<T> { }
class Y { }
class Z { }
class C : I<X<Y>>, I<Z> { }
public class P
{
	static void M<T>(I<X<T>> i) { } //Error
	public static void Main()
	{
		M(new C());
	}
}
```

## Syntax

### Inlining type constrains

> Source: [csharplang/discussions#279](https://github.com/dotnet/csharplang/discussions/279)

**Idea**

Do something similar to Java generic contraints definition

**Examples**

Introduced by [csharplang/discussions#279](https://github.com/dotnet/csharplang/discussions/279)

```c#
void F<T, U : IEnumerable<T>>(U source) {}
```

### Inference type argument list based on method signature

> Source: 
> [csharplang/discussions#352](https://github.com/dotnet/csharplang/discussions/352)

**Idea** 

Let compiler to deduce type arguments based on parameter list.

**Examples**

```c#
void Foo(var temp1, var temp2){} // = Foo<T1, T2>
```

### Shrotcut for using generic parameters used in inherited class

> Source: 
> [csharplang/discussions#6141](https://github.com/dotnet/csharplang/discussions/6141),
> [csharplang/discussions#3208](https://github.com/dotnet/csharplang/discussions/3208),
> [csharplang/discussions#1549](https://github.com/dotnet/csharplang/discussions/1549)

**Idea**

Introduce only new type parameters of class instead of repeating type arguments from inherited classes.

**Example**

```c#
class GenericA<T1, T2>{}
class GenericB<A> where A<A1, A2>: GenericA<A1, A2>{}
class GenericC<B> where B<A<T1, T2>>: GenericB<A>
```

## Overload analysis

> Source:
> [csharplang/issues#98](https://github.com/dotnet/csharplang/issues/98),
> [csharplang/discussions#1189](https://github.com/dotnet/csharplang/discussions/1189)

## Nullable analysis

### Converting out string?? to string?

> Source:
> [roslyn/issues#50782](https://github.com/dotnet/roslyn/issues/50782)


**Idea**

Allow to convert `string??` to `string?` during type inference.

### Nullable analysis of LINQ queries

> Source: 
> [csharplang/issues#3951](https://github.com/dotnet/csharplang/issues/3951)

**Idea**

Nullable analysis of LINQ queries (LDM expressed interested to handle Where, needs design proposal).

**Examples**

```c#
_ = list
    .Where(i => i is not null)
    .Select(i => i.ToString()); // should not warn
```

### Nullable analasis - tracking boolean logic

> Source:
> [csharplang/discussions#2388](https://github.com/dotnet/csharplang/discussions/2388)

**Idea**

Improve nullable analysis to reflect boolean track

**Examples**

```c#
static void Foo(object? first, object? second)
{
    Debug.Assert(first != null || second != null);
    if (first != null) {
        Console.WriteLine(first.GetHashCode());
    }
    else {
        Console.WriteLine(second.GetHashCode());
    }
}
```