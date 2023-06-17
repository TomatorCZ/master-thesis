# Improving type inference

## Summary

Allow a user to specify only necessary type arguments of

1. Generic method or local function call
2. Generic object creation

by introducing the `_` placeholder to mark type arguments inferred by the compiler.

Introduces 

1. inference using target type
2. inference using initializer list
3. inference using where clauses

of generic object creation.

## Motivation

The current method type inference works as an "all or nothing" principle. 
If the compiler is not able to infer command call type arguments, the user has to specify all of them. 
This requirement can be verbose, noisy, and unnecessary in cases where the compiler is able to infer almost all type arguments and need just to specify ambiguous ones. 
In these cases, we would like to give the compiler a hint for ambiguous type arguments. 
The current source of dependencies, which are used in type inference is restricted to method/function arguments which prevent making the whole type argument list inference in even simple scenarios. 
We could use the `_` placeholder for type arguments, which can be inferred from the argument list, and specify the remaining type arguments by ourselves. The potential additional sources of type information are specified below.

- **Inference by target type** - The current method type inference doesn't use target type for determining type arguments in inference resulting in specifying the whole argument list.

```csharp
object person = ...
int age = person.GetFieldValue("Age"); // Error: T can't be inferred

public static class ObjectExtensions {
    public static T GetFieldValue<T>(this object target, string field) { ... }
}
```

- **Inference by `where` clauses** - Utilizing `where` clauses to determine the type argument.

```csharp
using System.Collections.Generic;

var element = Foo(new List<int>()); //Error: TElem can't be inferred

TElem Foo<TList, TElem>(TList p) where TList : IEnumerable<TElem> {...}
```

- **Inference by later interaction with the object** - Utilizing later method calls or assignments to determine the type of the generic object (useful for object creation).

```csharp
var number = new Complex {RealPart = 1, ImaginaryPart = true}; // Error: TReal and TImaginary can't be inferred

public class Complex<TReal, TImaginary> 
{
    public TReal RealPart {get; set;}
    public TImaginary ImaginaryPart {get;set;}
}
```

Introducing improved method type inference involving the features above would bring breaking changes into the next C# version which we try to avoid.
However, there can be possibility in the future which would allow us to introduce breaking changes like this.
So we would like to already observe what dificulties involve to introduce better type inference like we know from RUST or Haskell.

For the first problem we would like to replace unsufficient type inference by giving the compiler hints about types and the obvious ones to let the compiler decide.
An example:

```csharp
var temp = ToCollection<List<_>, _>(1); // We are specifying the generic class, but its type argugument can be inferred by the compiler

TList ToCollection<TList, TElem>(TElem p1) where TList : IEnumerable<TElem> {...}
```

The example is goal-directed and introducing `_` doesn't bring very much. However, imagine that the element would be a type with long name. 
Or there can by more type arguments, which are obvious and only one needs to be specified. 
In these situations, `_` can save a lot of key strokes.
In cases where `_` is a name of type(very wierd idea), we will prioritize it and turn off the hints for the method type inferrer.
And because we are introducing a new concept of `_`, we doesn't introducing a breaking change.

For the second part of the problem, we can introduce constructor type inference, where we can try improved type inference by adding new type constraints.
It would also bring a braking change, however we can enable it just in case of used angle brackets (e.g. `new Klass<...>()`).
Because constructor type inference is not presented in the current C# version, we are free to experiment with it, improve the inferrer and use it later when the method type inference would be ready for this change.
Reasons for adding constructor type inference remain same, there are many types with more than 4 type parameters in standard library and there are not necessary to specify all of them in the type argument list.

### Possible extensions

Worth to mention other options which could be accomplished in the future regarding default and named type arguments.
Having the `_` placeholder can be used as a shortcut for choosing the right generic overload and to save typing when we use named type parameters.

```csharp
class Foo<T1, T2 = int> {}
class Foo<T1, T2 = int, T3 = string> {}

new Foo<T3: _, T2: string, T1 = _>(); // Assuming that T1 can be inferred and T3 is default.
new Foo(T2: string)(); // T1 and T3 are defaults
new Foo<_,_>(); // Choosing Foo<T1, T2> based on the arity
```

Method type inference(including object creation) is not the only place where we can use the `_` placeholder.
Sometimes `var` keyword as a variable declaration is not sufficient. 
We would like to be able to specify more the type information about variable but still have some implementation details hidden.
With the `_` placeholder we would be able to specify more the shape of the variable avoiding unnecessary specification of type arguments. 

```csharp
Wrapper<_> wrapper = ... // I get an wrapper, which I'm interested in, but I don't care about the type arguments, because I don't need them in my code.
wrapper.DoSomething();
```
At the end, casting could use the `_` placeholder as well.

```csharp
Foo<int, string> myvar = (Foo<_,_>)myobject; // Hint the type arguments based on target or other potential source of type information like default or named type arguments.
```

An interesting thing would be to allow the `_` placeholder in member lookup as you can see in the example below.
On the first see, it can look wierd.

```csharp
static class C1<T1> {
    static class C2 <T2> {
        public (T1, T2) Foo(T1 t, T2 t) {}
    }
}

var a = C1<_>.C2<_>.Foo(1, 1);
```

But in combination with default parameters, It might be useful in cases, where we use entity as a global provider of something, which we determine by type.

```csharp
static class Factory<T = Default> {
    public static T Create(){...}
}

int a = Factory<_>.Create(); // Calls Factory<int>.Create();
var b = Factory<_>.Create(); // Calls Factory<Default>.Create();
```

Although it is unlikely that it would be added into C# because of implementation complexity and hard readebility of code.

## Scope

Partial type inference can be solved in various ways.
We chose a feature enabling to hint the compiler by specifying ambiguous type arguments and letting the compiler infer the rest.
It aims at cases, where we want just to specify the arity of desired generic method(type) or specify a parameter that is not possible to infer from the context but let the compiler infer the remaining arguments. 

## Design

### Choosing the placeholder

We base our choose on usages specified below.

1. Type argument list of generic method call (e.g. `Foo<T1, T2>(...)`)
2. Type argument list of type creation (e.g. `new Bar<T1, T2>(...)`)
3. Type argument list of local variable (e.g. `Bar<T1, T2> temp = ...`)
4. Expressing array type (e.g. `T1[]`)
5. Expressing inferred type alone `T1` in local variable

**Diamond operator**

1. In the case of generic method calls it doesn't much make a sense since method type inference is enabled by default without using angle brackets.

```csharp
Foo<>(arg1, arg2, arg3); // Doesn't bring us any additional info
```

2. There is an advantage. It can turn on the type inference. However, it would complicate overload resolution because we would have to search for every generic type of the same name but no metter what arity. But could make a restriction. Usually, there is not more then one generic type with the same name. So when there will be just one type of that name, we can turn the inference on.

```csharp
new Bar<>(); // Many constructors which we have to investigate for applicability
new Baz<>(); // Its OK, we know what set of constructors to investigate.\

class Bar { ... }
class Bar<T1> { ... }
class Bar<T1, T2> { ... }

class Baz<T1,T2> {...}
```

3. It could make sense to specify just a wrapper of some type which gives us general API doesn't involving it's type arguments. It would say that the part of the code just care about the wrapper. However, we think that it doesn't give us much freedom because type arguments usually appears in public API and only few of them are for internal use. 

```csharp
Wrapper<> temp = ...
```

4. It doesn't seem very well.

```csharp
<>[] temp = ...
```

5. It clashes with `var` and looks wierd.

```csharp
<> temp = ... // equivalent to `var temp = ...`
```

**Whitespace seperated by commas**

1. it is able to specify arity of generic method. However, it seems to be messy when it is used in generic methods with many generic type parameters. Also it already have its own meaning of expressing open generic type.

```csharp
Foo<,string,List<>,>(arg1, arg2, arg3);
```

2. the same reasoning as above. However it doesn

```csharp
new Bar<,string,List<>,>(arg1, arg2) { arg3 };
```

3. It doesn't work with array type.

```csharp
Bar<,string,List<>,> temp = ...
```

4. It doesn't seems very well.

```csharp
[] temp = ...
Foo<,[],>(arg1, arg2)
```

5. It looks like CSharp it not staticly-typed langauge, clashed with `var` and probably introduce many implemenetation problems in the parser.

```csharp
temp = ...
```

**_ seperated by commas**

1. It specifies the arity of generic method. It explicitly says that we want to infer this type argument. It seems to be less messy.

```csharp
Foo<_, string, List<_>, _>(arg1, arg2, arg3);
```

2. The same reasons as above

```csharp
new Bar<_, string, List<_>, _>(arg1, arg2, arg3);
```

3. the same reasons as above.

```csharp
Bar<_, string, List<_>, _>(arg1, arg2);
```

4. Looks quite OK.

```csharp
_[] temp = ...
```

5. Clashes with `var` and seems to be wierd

```csharp
_ temp = ...
```

**var seperated by commas**

1. More key strokes. It starts to raise question if it brings an advantage of safe key strokes.

```csharp
Foo<var, string, List<var>, var>(arg1, arg2, arg3);
```

2. The same reasons as above

```csharp
new Bar<var, string, List<var>, var>(arg1, arg2, arg3);
```

3. the same reasons as above.

```csharp
Bar<var, string, List<var>, var>(arg1, arg2);
```

1. Looks OK.

```csharp
var[] temp = ...
```

5. State of the art

```csharp
var temp = ...
```

**Something else seperated by commas**

Doesn't make a lot of sense because it need to assign new meaning to that character in comparism with `_`, `var`. `<>`, `<,,,>`. Asterisk `*` can be considered, however it can remind a pointer. 

**Conslusion**

I prefer `_` character with enabling `<>` operator in case of constructor inference when there is only one generic type with that name. Additionaly to that, I would prohibit using `_` in the same places like `var`. 

### Nullable Annotation

Since we have nullable analysis, we could permit to specify nullability like this `_?`. 
However, we don't think it would have any significant benefits.

### Partial method type inference

For every generic method or function call, we will enable to use `_` as a placeholder for inferred type if there is no type if that name.
The placeholder can be nested (e.g. `G<_, List<_>>`).
The power of type inference remains same.

**Implementation**

1. Detecting inferred arguments

We will threat `_` in the same way as `var` keyword. 
We will make a special symbol `SourceInferredTypeArgumentSymbol`.
When we will bind an invocation expression, we will specially threat the `_` placeholder. 
During investigating of method simple name (not containing name of class), if the look up doesn't find any type symbol, we will create `SourceInferredTypeArgumentSymbol` instead of raising an error of unknown type name.
It will prioritize usage of `_` as type name (type parameter name, struct or class name) instead of threatening it as `SourceInferredTypeArgumentSymbol` which will doesn't change behavior of code compiled by previous version of the compiler.  

2. Conditions for type inference

So we have type argument list and entering into the overload resolution. 
We want to infer type parameters if the method is generic, its argument list doesn't contain any dynamic argument and doesn't have type argument list or type argument list contain `SourceInferredTypeArgumentSymbol`. 
It doesn't matter how nested it is. We don't have to check if the receiver is dynamic because there is now overload resultion in that case.

3. Type inference

Description of type inference will be presented in type inference of constructors.

4. Checking generic method calls or functions involving `dynamic` keyword 

For type arguments, which doesn't contain any `SourceInferredTypeArgumentSymbol` (even nested), are substituted in parameter lists. 
Those parameters, which doesn't contain any type parameter, are checked with corresponding arguments. Checking involves respecting the type parameters' constraints and applicability of arguments. 

5. Nullable analysis.
   
The condition for entering into type inference is similar to the second point. 
We have to bind the type arguments again with information about nullability and run the inference in the same manner as the current version of the compiler with adjusted type inferrer which will be described later.

**Examples**

> Common use cases

```csharp
// Inferred: [TCollection = List<MySuperComplicatedElement<Arg1, Arg2>>, TElem = MySuperComplicatedElement<Arg1, Arg2>]
// Use case: Specifying just a type of collection because other arguments can be inferred. Sometimes, the `where` constraints are crucial for the type inference. In that case we will use the hint because type inference is not so powerful.
var temp1 = ToCollection<List<_>, _>(new MySuperComplicatedElement<Arg1, Arg2>()); 
// Inferred: [TResult = MyResult, TAlgorithm = MyAlg, TOptions = MyAlgOpt, TInput = MyInput]
// Use case: Most of the type arguments can be inferred from arguments. Sometimes the return type contains type paremeter as well and can be crucial for the type inference. In that case, we will use the hint because type inference is not so powerful
MyResult temp2 = Run<_,_,_,MyResult>(new MyAlg(), new MyAlgOpt(), new MyInput());
// Inferred: [TPressision = double]
// Use case: Type parameters can be used for internal usage. In that case we would like to provide the compiler hint 
Result temp3 = Computation<double, _, Result, _>(new Data(), new Opts());

// Definitions
TCollection ToCollection<TCollection, TElem>(TElem p1) where TCollection : IEnumerable<TElem> { ... } 
TResult Run<TAlgorithm, TOptions, TInput, TResult>(TAlgorithm alg, TOptions opts, TInput input) { ... }
Result Computation<TPressision, TData, TResult, TOpts>(TData data, TOpts opts) { ... } 
```

> Tests

```csharp
F1<_, string>(1); // Inferred: [T1 = int, T2 = string] Simple test
F2<_,_>(1,""); // Inferred: [T1 = int, T2 = string] Choose overload based on arity
F3<int, _, string, _>(new G2<string, string>); // Inferred: [T1 = int, T2 = string, T3 = string, T4 = string] Constructed type
F4<_, _, string>(x => x + 1, y => y.ToString(),z => z.Length); // Inferred: [T1 = int, T2 = int, T3 = string] Circle of dependency
F5<string>(1); // Inferred: [T1 = string] Expanded form #1
F5<_>(1, ""); // Inferred: [T1 = string] Expanded form #2
F5<_>(1, "", ""); // Inferred: [T1 = string] Expanded form #3

B1<int> temp1 = null;
F6<A1<_>>(temp1); // Inferred: [ T1 = A1<int> ] Wrapper conversion

B2<int, string> temp2 = null;
F6<A2<_, string>>(temp2); // Inferred: [ T1 = A2<int, string> ] Wrapper conversion with type argument

C2<int, B> temp3 = null;
F6<I2<_, A>>(temp3); // Inferred: [ I2<int, A> ] Wrapper conversion with type argument conversion

dynamic temp4 = "";
F7<string, _>("", temp4, 1); // Inferred: [T1 = int] Error: T1 = string & int
F7<_, string>(1, temp4, 1); // Inferred: [T1 = int] Warning: Inferred type argument is not supported by runtime (type hints will not be used at all)
temp.F7<string, _>(temp4);  // Inferred: [T1 = int] Warning: Inferred type argument is not supported by runtime (type hints will not be used at all)

F1<_,_>(""); // Error: Can't infer T2
F1<_,int>(""); // Error: Can't infer T2
F1<_,byte>(257); // Error: Can't infer T2

#nullable enable
string? temp5 = null;
string temp6 = "";
C2<int, string> temp7 = new C2<int, string>();
C2<int, string?> temp8 = new C2<int, string?>();
C2<string, int> temp9 = new C2<string, int>();

F8<int, _>(temp5); // Inferred: [T1 = int, T2 = string!] 
F8<int, _>(temp6); // Inferred: [T1 = int, T2 = string!] 
F8<int?, _>(temp5); // Inferred: [T1 = int?, T2 = string!] 
F8<int?, _>(temp6); // Inferred: [T1 = int?, T2 = string!] 
F9<int, _>(temp5); // Inferred: [T1 = int, T2 = string?] 
F9<int, _>(temp6); // Inferred: [T1 = int, T2 = string!] 
F9<int?, _>(temp5); // Inferred: [T1 = int?, T2 = string?] 
F9<int?, _>(temp6); // Inferred: [T1 = int?, T2 = string!] 


F10<I2<_, string?>>(temp7); // Inferred: [T1 = I2<int, string?>!] Can convert string to string? because of covariance
F10<C2<_, string?>>(temp7); // Error: Can't convert string? to string because of invariance
F10<I2<_, _>>(temp7); // Inferred: [T1 = I2<System.Int32, System.String!>!]
F10<C2<_, _>>(temp7); // Inferred: [T1 = C2<System.Int32, System.String!>!]
F10<I2<_, _>>(temp8); // Inferred: [T1 = I2<System.Int32, System.String?>!]
F10<C2<_, _>>(temp8); // Inferred: [T1 = C2<System.Int32, System.String?>!]
F10<I2<_, string>>(temp8); // Error: Can't convert string? to string because of covariance
F10<C2<_, string>>(temp8); // Error: Can't convert string? to string because of invariance
F10<I2<string?, int>>(temp9); // Inferred: [T1 = C2<System.Int32, System.String?>!] Can convert string to string? because of contravariance

void F8<T1, T2>(T2? p2) { }
void F9<T1, T2>(T2 p2) { }
void F10<T1>(T1 p1) {}
#nullable disable

//Definitions
void F1<T1, T2>(T1 p1) {}
void F2<T1, T2>(T1 p1, T2 p2) {}
void F2<T1>(T1 p1, string p2) {}
void F3<T1, T2, T3, T4>(G2<T2, T4> p24) {}
class G2<T1, T2> {}
void F4<T1, T2, T3>(Func<T1, T2> p12, Func<T2, T3> p23, Func<T3, T1> p31) { }
void F5<T>(int p1, params T[] args) {}
void F6<T1>(T1 p1) {}
class A {}
class B : A{}
class A1<T> {}
class A2<T1, T2> {}
class B1<T> : A1<T> {}
class B2<T1, T2> : A2<T1, T2> {}
interface I2<in T1, out T2> {}
class C2<T1, T2> : I2<T1, T2> {}
void F7<T1, T2>(T1 p1, T2 p2, T1 p3) {}
void F11<T1, T2>(T2 p2) { }

//Seperated Assembly
F1<_> (null); // Inferred: [T1 = _] class `_` turned the inference off

F1<T1>(T1 p1) {}
class _ {}
```

### Type inference of constructor

As we mentioned in the motivation, we will experiment with improving type inference in object creation.
The inference will influence following expressions.

1. Object creation
2. Array creation

Beside mentioned partial type inference, we will include information about target type and initializer list together with type parameter constraints.

**Implementation**

1. Detecting inferred arguments

We will theat `_` in the same way as in the method type inference.
Special handling of `_` will be turn on when we will bind `ClassCreationExpression` or `ArrayExpressionExpression`. 
That means, we will not to support it in the `DelegateCreationExpression`.

2. Conditions for type inference

The inference is entered when type argument list exist however doesn't constain any type parameters (diamond operator`<>`), or type argument list contains `SourceInferredTypeArgumentSymbol`.

3. Checking constructor call involving `dynamic` keyword 

For type arguments, which doesn't contain any `SourceInferredTypeArgumentSymbol` (even nested), are substituted in parameter lists. 
Those parameters, which doesn't contain any type parameter, are checked with corresponding arguments. Checking involves respecting the type parameters' constraints and applicability of arguments. 

4. Array type inference

We changes best common type of set of expressions by adding constraint from type argument list of array creation (e.g. `new G<_>[] {...}`, `G<_>` is added to the set of bounds(We will describe how to handle `_` and in which bounds should be `G<_>` in the description of constructor type inference)).
We also add constraint from target type (e.g. `IEnumerable<int> temp = new [1]`, Do upper bound inference of `IEnumerable<int>` and `T[]`).
This will ensure, that type information form target and type argument list is added to the inferrer.
We don't have to care about constructor and where clauses because they don't help us to find type argument and initializer is already used in best common type algorithm.

For the rest of the points we will use the following diagram to better describe the process.

> Object creation binding

![ObjectCreationBinding](./../Artifacts/ObjectCreationExpressionBinding_Improved.drawio.png)

5. Passing information about target type

Information about the target can come from two places. 
The first place is Variable declaration statement. 
In that case we bind the variable if we can (e.g. it is not `var`) and pass it to bind the declarator expression. The second place can be parameter type. 
In that case it is somehow tricky. There are three possible scenarios. 
We already know type of the parameter (It doesn't contain any type parameters). We don't know type of the parameter because it contains type parameter, although after the inference, we will know it. 
And the last option is when type inference fails and doesn't find type arguments. 
We will describe how target type is passed in these scenarios in the following paraghraphs. 

6. Binding arguments

There can be situation, when argument is another creation expression which needs type inference.
In the time of arguments binding, we would like to know parameter types because of passing it into the binding process. 
We can do it by postponing binding of `ObjectCreationExpression`s and `ArrayCreationExpression`s to the time, when we start to investigate each of the constructor candidate. 
In that time we already have exact list of the parameters. 
So in the binding process before constructor overload resolution, we will convert above mentioned expression into `BoundUnconvertedObjectCreationExpression` and `BoundUnconvertedArrayCreationExpression`. 
When we arrive in front of checking applicability of canstructor. We will look at approprite type of parameters and do the following. If the argument is on of "BoundUnvonverted" expressions and the type of parameter doesn't contain type parameter, bind the expression with passing the info about the target. Otherwise try to bind it without info about target type, if it succeeds, use the type in further inference, if not, wait after the inference. It can happen, that the inference still succeded and we can try to bind the argument again with already determined type of target. 

> Note: Of cource we could do here better and use more info about target even we don't know the exact type. 
> However it would complicated the algorithm because of overloads. 
> So We propose more simple alternative, which still have benefits in common use cases.

7. Getting information from initializer

This step is a litte bit complicated because of overloads. 
If it is a collection initializer and there are more than one overload of `Add` method, we don't know which parameter type constraint to use till its overload resolution. However, we think having overloads of `Add` method is not very common. So we could do the following. 
If it is a Object initializer, Array initializer, collection initializer with only one method `Add` or collection initializer using indexer with only one indexer, we will collect the argument type constraints. 
Because in that case, we know that are not any other possible constraints which should hold. 
So we will go through all initializers and if the initializer parameter type contains type parameter, we collect the argument types contraint in the same manner as in the constructor argument list.

8.  Coercing arguments

After we infer type parameters of the type and choose right constructor, we have to try binding "UnconvertedExpressions" again with target type info and convert it into proper Bound nodes.

9.  Bindining initializer list

Then we procced as usual to bind the initializer list.

10.  Constructor inference

> Type inference

![TypeInference](./../Artifacts/MethodTypeInferrer_Improved.drawio.png) 

We will extend the API to enable to obtain info about target type, type argument hints (e.g. `Foo<_, G<_>, int>()`), parameter constraints from initializers (e.g. `Add(T p1)`, `Bar<_> {1,2}` so constraint is `int = T`). And list of type parameters. 

We will also create another type of type variable which would be `SourceInferredTypeArgumentSymbol` and threat it in the same manner as with Type parameters. 
We introduce new bound of type `Shape` which doesn't allow to be converted into different type (e.g. `string` shouldn't be permited to be converted to `string?`).
 And we add type arguments to corresponding bound shape of type parameter. 
 
 Because from now on the bounds can contain unfixed type variables. 
 We introduce two new types of dependencies. 
 The first on is `TypeVarDependency` which holds the info if given type variable contains in its bounds(exluding shape bound) givin type variable. 
 The second dependency is `ShapeVarDependency` and its same just for the shape bound.

We then run the firt phase as usual. 
When we enter `AddBound` momment. We have to be careful here. 
The adding bound can contain type variable or already inferred bounds can contain type variable.
We want to propagate these type dependencies also into that type variables.
So for bound containing type variable, we will run the inference with either adding bound(if the bound containing the type variable is not the bound which we are adding) or each of bounds of type variable(if it is the adding bound.) We will respect the kind of bonds in inferences. 
It can happen the we would have type variable at both sides of the source, target pair. 
in that case we will ignore the type variables in source and just skip it. 
We don't lose the dependency because we run the inferences for each of pair containing type variable in the source.

Durring the second phase, we have to be careful about dependencies. We can't fix type variable which is `ShapeVarDependent` on unfixed type variable. However we will allow to fix type variable which is  `TypeVarDependent` on another type variable, however has at least one bound which doesn't contain any unfixed type variables. We will allow it only in that moment, when there will not be any unfixed type variable which is not dependent. This will prevent situation, where there is circular dependency between type variables, but there are also another sources of type info which can "break" this circle.

Fixing is done in usual way with one exception. When the type variable has shape bound. We have to keep the type exactly same and just check if it is ok with other constrains.

After the Type inference, we receive infered type parameters

**Examples**



11.   Nullable analysis

Because in the current C# version there is no constructor overloading, there is no need for rewriting the types genereted from constructors.
However, now it can happen that the inference infer some different type(with different nullability, or failed because of that).
In this situations, we have to rewrite type genereted by the constructor if constructor type inference found different type.

**Examples**

> Common use cases

```csharp
using System.Collections.Generic;

// Inferred: [T = int] Assuming that there are no other generic type with `List` name
// Use case: We want to determine type of the element by initializer list.
var temp1 = new List<>{ 1, 2, 3}; 

// Inferred: [TKey = string, TValue = int]
// Use case: Doesn't matter how the add method looks like
var temp2 = new Dictionary<_,_>{ {"key", 1} };

// Inferred: [T = int]
// Use case: Type parameters can be determinded by target type
IEnumerable<int> temp2 = new List<>();

// Inferred: [Tuple<string, int>[]]
// Use case: Information about target type can be "forwarded" into the nested expressions
IEnumerable<Tuple<string, int>> temp3 = new[] { new("",1 ) };

// Inferred: [T = int]
// Using type hints in type argument list
new C1<_>[] {new C2<int>()};

// Inferred: [T = int]
// Use case: Information about the target is propagated even in generic calls
Foo(new List<>(), 1);

//Inferred: [TKey = string, TValue = int]
// Use case: Using indexers to determine type parameters
var temp4 = new Dictionary<_,_>()
{
    ["foo"] = 34,
    ["bar"] = 42
};

// Inferred: [TCollection = List<int>, TElem = int]
// Use case: Using where constraint to determine other type parameters.
var temp5 = new Bag<_,_>(new List<int>());

// It is possible to combile info from several soruces (target type, initializer list, type arguemnt list, constructor, where constraint)

//Declarations
class Dictionary<T1, T2, T3> {}
void Foo<T>(IEnumerable<T> p1, T p2) {}
class Bag<TCollection, TElem> where TCollection : IEnumerable<TElem>
{
    public Bag(TCollection collection) {}
}
```

> Tests

> Similar to Method type inference

```csharp
new C1<_, string>(1); // Inferred: [T1 = int, T2 = string] Simple test
new C2<_,_>(1,""); // Inferred: [T1 = int, T2 = string] Choose overload based on arity
new C3<int, _, string, _>(new G2<string, string>); // Inferred: [T1 = int, T2 = string, T3 = string, T4 = string] Constructed type
new C4<_, _, string>(x => x + 1, y => y.ToString(),z => z.Length); // Inferred: [T1 = int, T2 = int, T3 = string] Circle of dependency
new C5<string>(1); // Inferred: [T1 = string] Expanded form #1
new C5<_>(1, ""); // Inferred: [T1 = string] Expanded form #2
new C5<_>(1, "", ""); // Inferred: [T1 = string] Expanded form #3

B1<int> temp1 = null;
new C6<A1<_>>(temp1); // Inferred: [ T1 = A1<int> ] Wrapper conversion

B2<int, string> temp2 = null;
new C6<A2<_, string>>(temp2); // Inferred: [ T1 = A2<int, string> ] Wrapper conversion with type argument

C2<int, B> temp3 = null;
new C6<I2<_, A>>(temp3); // Inferred: [ I2<int, A> ] Wrapper conversion with type argument conversion

dynamic temp4 = "";
new C7<string, _>("", temp4, 1); // Inferred: [T1 = int] Error: T1 = string & int
new C7<_, string>(1, temp4, 1); // Inferred: [T1 = int] Warning: Inferred type argument is not supported by runtime (type hints will not be used at all)

F1<_,_>(""); // Error: Can't infer T2
F1<_,int>(""); // Error: Can't infer T2
F1<_,byte>(257); // Error: Can't infer T2

#nullable enable
string? temp5 = null;
string temp6 = "";
GC2<int, string> temp7 = new GC2<int, string>();
GC2<int, string?> temp8 = new GC2<int, string?>();
GC2<string, int> temp9 = new GC2<string, int>();

new C8<int, _>(temp5); // Inferred: [T1 = int, T2 = string!] 
new C8<int, _>(temp6); // Inferred: [T1 = int, T2 = string!] 
new C8<int?, _>(temp5); // Inferred: [T1 = int?, T2 = string!] 
new C8<int?, _>(temp6); // Inferred: [T1 = int?, T2 = string!] 
new C9<int, _>(temp5); // Inferred: [T1 = int, T2 = string?] 
new C9<int, _>(temp6); // Inferred: [T1 = int, T2 = string!] 
new C9<int?, _>(temp5); // Inferred: [T1 = int?, T2 = string?] 
new C9<int?, _>(temp6); // Inferred: [T1 = int?, T2 = string!] 


new C10<I2<_, string?>>(temp7); // Inferred: [T1 = I2<int, string?>!] Can convert string to string? because of covariance
new C10<C2<_, string?>>(temp7); // Error: Can't convert string? to string because of invariance
new C10<I2<_, _>>(temp7); // Inferred: [T1 = I2<System.Int32, System.String!>!]
new C10<C2<_, _>>(temp7); // Inferred: [T1 = C2<System.Int32, System.String!>!]
new C10<I2<_, _>>(temp8); // Inferred: [T1 = I2<System.Int32, System.String?>!]
new C10<C2<_, _>>(temp8); // Inferred: [T1 = C2<System.Int32, System.String?>!]
new C10<I2<_, string>>(temp8); // Error: Can't convert string? to string because of covariance
new C10<C2<_, string>>(temp8); // Error: Can't convert string? to string because of invariance
enw C10<I2<string?, int>>(temp9); // Inferred: [T1 = C2<System.Int32, System.String?>!] Can convert string to string? because of contravariance

class C8<T1, T2>
{
    public C8(T2? p2) { }
}
class C9<T1, T2>
{
    public C9(T2 p2) { }
}
class C10<T1>
{
    public C10(T1 p1) {}
}
#nullable disable

//Definitions
class C1<T1, T2> 
{
    public C1(T1 p1) {}
}
class C2<T1, T2> 
{
    public C2(T1 p1, T2 p2) {}
}
class C2<T1> 
{
    public C2(T1 p1, string p2) {}
}
class C3<T1, T2, T3, T4> 
{
    public C3(G2<T2, T4> p24) {}
}
class G2<T1, T2> {}
class C4<T1, T2, T3>
{
    public C4(Func<T1, T2> p12, Func<T2, T3> p23, Func<T3, T1> p31) {}
}
class C5<T1> 
{
    public C5(int p1, params T1[] args) {}
}
class C6<T1> {
    public C6(T1 p1) {}
}
class A {}
class B : A{}
class A1<T> {}
class A2<T1, T2> {}
class B1<T> : A1<T> {}
class B2<T1, T2> : A2<T1, T2> {}
interface I2<in T1, out T2> {}
class GC2<T1, T2> : I2<T1, T2> {}
class C7<T1, T2> 
{
    public C7(T1 p1, T2 p2, T1 p3) {}
}
class C11<T1, T2> 
{
    public (T2 p2) { }
}

//Seperated Assembly
new C1<_> (null); // Inferred: [T1 = _] class `_` turned the inference off

class C1<T1> 
{
    C1(T1 p1) {}
}
class _ {}
```

> Target-typed, Constrains, Initializers

```csharp
using System.Collections.Generic;
// Inferred: [T = int]
// Target type(type of variable declaration) is passed to type inference of constructor
C1<int> temp1 = new C2<_>();

// Inferred: [T = int]
// Target type is passed to type inference of constructor after overload resolution of `F1<T>`
F1(new C2<_>(), 1);

//Inferred: [T = int]
//It works for constructors and initializers as well
new C3<_>(new C2<_>(), 1);

// Inferred: [T = int] 
IEnumerable<int> temp2 = new[1];

//Inferred: [T = C2<int>]
IEnumerable<C1<int>> temp3 = new C2<_>[1];

// Inferred: [T1 = C1<int>, T2 = int]
// Using constraints to determine type parameters
new C4<_,_>(1);

// Inferred: [T1 = int, T2 = string]
// Using Object intializer list to determine type parameters
new C5<_,_> {Prop1 = 1, Prop2 = ""}; 

//Inferred: [T1 = 1, T2 = string]
//Using Collection Initializer list to determine type parameters
new C6<_,_> {{1, ""}};

// Error: Can't infered because Add method has overloads.
new C7<_,_> {1, "" }; 

//Inferred: [T1 = int, T2 = string]
// Using indexers to determine type parameters
new C8<_,_> 
{
    ["A"] = 1;
};

// Inferred: [T1 = int, T2 = int, T3 = int, T4 = int, T5 = int, T = int]
// Combinining type constraints from target, constructor, type argument list, object initializer and where clause to determine type of parameters
F1(new C9<_,_,_,int,_>(1) {Prop1 = 1},1);

class C1<T> {}
class C2<T> : C1<T> {}
void F1<T>(C1<T> p1, T p2) {}
class C3<T>
{
    public C3(C1<T> p1, T p2) {}
}
class C4<T1, T2> where T1 : C1<T2> 
{
    public C4(T2 p1) {}
}
class C5<T1, T2>
{
    public T1 Prop1 {get;set;}
    public T2 Prop2 {get;set;}
}
class C6<T1, T2> : IEnumerable 
{
    ...
    public void Add(T1 p1, T2 p2) {} 
}
class C7<T1, T2> : IEnumerable
{
    ...
    public void Add(T1 p1) {}
    public void Add(T2 p2){}
}
class C8<T1, T2>
{
    public T1 this[T2 p1] {get {throw new NotImplementedException();} set {throw new NotImplementedException();}}
}
class C9<T1, T2, T3, T4, T5> : C1<T3> where T5 : C1<T4>
{
    public C9(T1 p1) {}
    public T2 Prop1 {get;set;}
}
``` 