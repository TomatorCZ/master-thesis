# Explicit partial type inference

## Summary

Allow an user to specify only necessary type arguments of

1. Generic method or local function call
2. Generic object and delegate creation
3. Variable declaration of a generic type
4. Cast

by introducing the `_` placeholder to mark type arguments inferred by the compiler.

## Motivation

The current method type inference works as an "all or nothing" principle. 
If the compiler is not able to infer command call type arguments, the user has to specify all of them.
This requirement can be verbose, noisy, and unnecessary in cases where the compiler is able to infer almost all type arguments and need just to specify ambiguous ones.
In these cases, we would like to give the compiler the hint for ambiguous type arguments.
The current source of dependencies, which are used in type inference is restricted to method/function arguments which force to make the whole type argument list inference in even simple scenarios.
We could use the `_` placeholder for type arguments, which can be inferred from the argument list, and specify the remaining type arguments by ourselves.
The potential additional sources of type information are specified below.

- **Inference by target type** - The current method type inference doesn't use target type for determining type argument in inference resulting in to specify the whole argument list. 
- **Inference by `where` clauses** - Utilize `where` clauses to determine the type argument.
- **Inference by later calls** - Utilize later method calls to determine the type of the generic object (useful for object creation).
- **Inference by inheritance or implementation** - Utilize implemented interfaces or inherited class for determining type argument.

Because of restricted type inference, `_` can be used only in restricted scenarios.
However, the potential of this feature with combination of mentioned type inference improvements can bring more useful cases.
See the following examples, where `_` can be used to hint the compiler type arguments, whih could be infered in case of implemented mentioned inference improvements.

```csharp
TResult Foo<TParam, TResult>(TParam p)
where TResult ...
{ ... }

MyResult res = Foo<_, MyResult>(myVar); // We can hint the compiler the target type
```

```csharp
void Foo<TList, TElem>(TList p) 
where TList : List<TElem>
{}

Foo<_, int>(new List<int>()) // we can hint the compiler info from `where` clauses
```

```csharp
class Foo<T1, T2> {
    Foo(T1 p) {}
    public void Bar(T2 p) {}
}

var a = Foo<_, int>("str"); // we can hint the compiler type arguments obtained from later calls
a.Bar(1);
```

```csharp
class Foo<TElem, TParam> : IEnumarable<TElem> {
    Foo(TParam p) {}
}

IEnumerable<int> = new Foo<int, _>(42); // we can hint the compiler type argument obtained from interface implementation or class inheritance
```

Even if all of these sources would be use during method inference, there will be still type arguments, which can't by inferred from the context.
We are talking about type arguments, which are used internaly in the class or struct and are not exposed to global API.
So It would be still beneficial to have the `_` placeholder.

As you could see in the example, we see potential to use type inference in constructors.
The motivation behind this can be to specify just the wrapper implementation and let the elements inside the wrapper by inferred.
It would help to make documentation right by the code with meaning "This segment of code primary cares about the wrapper".

```csharp
var a = new Wrapper<_>(wrappedElement)
```

Another need where `_` placeholders can be used is specifying arity of type argument on places, where there can be amnigituities like `IEnumerable` vs `IEnumerable<_>`.

Worth to mention other options which could be accomplished in the future regarding default and named type arguments.
Having `_` placeholder can be used as a shortcut for choosing right generic overload and save typeing when we use named type parameters.

```csharp
class Foo<T1, T2 = int> {}
class Foo<T1, T2 = int, T3 = string> {}

new Foo<T1: _, T2: string, T3 = _>() // assuming that T1 can be inferred T3 is default.
new Foo<_,_> // choosing Foo<T1, T2> based on arity
```

Method type inference(including object creation) is not the only place where we can use the `_` placeholder.
Sometimes `var` keyword as a variable declaration is not sufficient. 
We would like to be able to more specify the type informaton about variable but still have some implementatino details hided.
With the `_` placeholder we would be able to specify more the shape of variable avoiding necessary.  

```csharp
Wrapper<_> wrapper = ... // I get an wrapper, which I'm interested in, but I don't care about the type arguments, because I don't need them in my code.
wrapper.DoSomething();
```
At the end, casting could use the `_` placeholder as well

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
    public T Create(){...}
}

int a = Factory<_>.Create(); // Calls Factory<int>.Create();
var b = Factory<_>.Create(); // Calls Factory<Default>.Create();
```

Although it is inlikely that it would be added into C#, we would like to investigte what should be done to enabling it for deeper understanding type inference in Roslyn.

Based on mentioned advantages which `_` placeholder can bring to type inference we feel to add it to C# worthly.

## Scope

Partial type inference can be solved in various ways.
We chose a feature enabling to hint the compiler by specifying amigitious type arguments and let the remaining ones on him.
It aims on cases, where we want just to specify the arity of desired generic method(type) or specify a parameter that is not possible to infer from the context but let the compiler infer the remaining arguments. 

## Design

As we mentioned in the emotivation, we see the usage of our proposal in the following places.

1. method call
2. object construction
3. variable declaration
4. casting

### Method call

We would like to somehow mark inferred type arguments. 
The most natural approach would be to replace the type arguments with some keyword. 
Let's pick `_` for it for now and show it in the examples.

```csharp
void foo<T1, T2, T3>(T2 arg) {}

foo<int, _, int>("string"); // _ is marked as "inferred by compiler"
```

One can suggest not marking it and just leaving it on the compiler. 
However, it would bring ambiguities between which arguments should be inferred. 

```csharp
void foo(int arg1, int arg2) {}
void foo<T1>(T1 arg1, int arg2) {}
void foo<T1, T2>(T1 arg1, T2 arg2) {}

foo(1,2); // We don't know which overload to choose
foo<>(1,2); // Still, it is not clear which generic overload should be choose.
```

Because of mentioned issues, we believe that marking each type argument, what the compiler will infer, is the right way how to do it.
To make it possible, we would have to change C# grammar

```
invocation_expression
    : primary_expression '(' argument_list? ')'
    : simple_name_u '(' argument_list? ')'
    : member_access_u '(' argument_list? ')'
    : null_conditional_member_access_u '(' argument_list? ')'
    ;

simple_name_u
    : identifier type_argument_list_u?
    ;

member_access_u
    : primary_expression '.' identifier type_argument_list_u?
    | predefined_type '.' identifier type_argument_list_u?
    | qualified_alias_member '.' identifier type_argument_list_u?
    ;

null_conditional_member_access_u
    : primary_expression '?' '.' identifier type_argument_list?
      dependent_access* dependent_member_access_u?
    | primary_expression '?' '.' identifier type_argument_list_u?
    ;
    
dependent_access
    : '.' identifier type_argument_list?    // member access
    | '[' argument_list ']'                 // element access
    | '(' argument_list? ')'                // invocation
    ;

dependent_member_access_u
    : '.' identifier type_argument_list_u?    // member access
    ;

type_argument_list_u
    : '<' type_arguments_u '>'
    ;

type_arguments_u
    : type_argument_u (',' type_argument_u)*
    ;   

type_argument_u
    : type | '_'
    ;

```

To be able to use it in invocation, we have to modify method invocation and type inference algorithm.

> #### 11.8.9.2 Method invocations
>
> ...
>
> - **If F is generic and M includes a type argument list containing at least one `_` placeholder, F is a candidate when:**
>   - **F has the same number of method type parameters as were supplied in the type argument list, and**
>   - **Type inference (§11.6.3) succeeds, inferring a list of type arguments for the call, and**
>   - **Once the type arguments are substituted for the corresponding method type parameters, all constructed types in the parameter list of F satisfy their constraints (§8.4.5), and the parameter list of F is applicable with respect to A (§11.6.4.2).**
> - If F is generic and M includes a type argument list, F is a candidate when:

> #### 11.6.3.1 General
> 
> ...
> 
> When a particular method group is specified in a method invocation, and either no type arguments are specified as part of the method invocation or the type argument list contains at least one '_', type inference is applied to each generic method in the method group.
>
> ...
>
> With a method call of the form `M(E₁ ...Eₓ)` **or `M<Y₁...Yᵥ>(E₁ ... Eₓ)`** the task of type inference is to find unique type arguments S₁...Sᵥ for each of the type parameters X₁...Xᵥ so that the call `M<S₁...Sᵥ>(E₁...Eₓ)` becomes valid **and in the case of `M<Y₁...Yᵥ>(E₁ ... Eₓ)` type argument `Sk` should by identical with `Yk` if Yk is not `_`**.
>
> ...
>
> #### 11.6.3.2 The first phase
> **If the method call has the form `M<Y₁...Yᵥ>(E₁ ... Eₓ)` for each `Yᵢ` which is not `_`, fix type variable `Xᵢ` to `Yᵢ`.**
> For each of the method arguments `Eᵢ`:

If we consider dynamic overload resolution, we modify compile-time checks to reflect the change.

> 12.6.5 Compile-time checking of dynamic member invocation
>
> First, if F is a generic method and type arguments were provided, then those **which are not `_`** are substituted for the type parameters in the parameter list. However, if type arguments were not provided, no such substitution happens.


There raises an question if we can go furthur and allow `_` to be nested 
See the following example.

```csharp
TResult GetResult<TResult>() where TResult : IEnumerable<string> {}

var results = GetResult<List<_>>(); // Just specifying wrapper implementation.
```

It would be mostly beneficial during working with any generic wrappers, where we are curious about the wrapper implementation, but not which will be inside the wrapper.

This kind of wildcard needs more information about the relation between type arguments to become useful.
More information can be extracted from generic type constraints, which can bring also more inference power to simple `_` inferred types.
Unfortunately, using information about constraints in type inference causes breaking change, see the following example.

```csharp
void M(object) {}
void M<T, U>(T t) where T : IEnumerable<U> {}

M("foo");
```

One option would be to somehow differ between old inference and new improved inference like that.

```csharp
void M(object) {} // 1.
void M<T, U>(U t) where T : IEnumerable<U> {} // 2.

M<_, _>("foo"); // calls 2.
M("foo"); // calls 1.
```

Because of this breaking change, we can use the constraints only with `_` inferred types, which is not presented in the current language version so it doesn't cause a breaking change.

One can feel that `M<_,_>(...)` should be resolved in the same manner as `M(...)`. 
This is a reasonable argument and we feel that it should follow it.
For this reason, we would postpone this extended feature to future improvements where we would be able to use the constraints without causing a breaking change.
This could be done by some tool that patches the old code.

#### Object construction

We have 2 `creation_expressions`, where we can potentially use `_` placeholder.

1. object
2. delegate

Firtly, we will look at an object creation.
Currently, C# support only target-typed `new` operator.
However, we would like to create a generic object based on arguments only allowing us to choose the *implementation*.
See the example

```csharp
IEnumerable<int> myVar = new List<_>() {1,2,3};
// We feel necessary to enable inference even there are no type arguments to be consistent with method type inference
myVar = new List() {1,2,3};
```

The curent overload resolution of constructors is bounded to constructors of already known type (closed type).
We have to change how the list of constrictor candidates is selected.
Then we can run the same method type inference and use overload resolution to pick the right type with the constructor.
Unfortunetly, beside compile-time binding, there is also dynamic-type binding which is a part of CLR.
Making the change only in the compiler will result in the following confusing behavior.

```csharp
class Foo<T1, T2> {
    public Foo(T1 p1, T2 p2) {}
}
dynamic myVar = new object();
object myObj = new object();
new Foo(myObj, myObj); // OK - compile-time binding, in Roslyn
new Foo<_,_>(myObj, myObj); // OK - compile-time binding, in Roslyn
new Foo(myVar, myVar); // Failed in dynamic-time binding, in CLR
new Foo<_,object>(myVar); //Failed in dynamic-time binding, in CLR
new Foo<_,_>(myVar); //Failed in dynamic-time binding, in CLR
```

> TODO: Propose change also to CLR ??

However, we think that the change is worth to implement, so we propose the change for the compiler. 
We would allow to use `_` only in `object_creation_expression` and `delegate_creation_expression`.
To make it possible we have to change the grammar.
For abbreviation, we refer a type name allowing to contain `_` in its type arguments to `type_name_u`.
(`type_name_u` can be `Foo<_, int>` but not `Foo1<int, _>.Foo2<int,_>`) 

```
object_creation_expression
    : 'new' type_u '(' argument_list? ')' object_or_collection_initializer?
    | 'new' type_u object_or_collection_initializer
    ;

delegate_creation_expression
    : 'new' delegate_type_u simple_type_name_u? '(' expression ')'
    ;
```

The second change would be to determine the constructor.
We have to ensure that types without type parameters will be preffered in situations where *object_creation_expression* has no type arguments to prevent a breaking change.
According to specification, let's define a *Type group*.
*Type group* is a set of types (structs or classes) having the same (fully-quolified) name but differing in number of type parameters which they have.
The type group always contain only *value_types* or only *class_types*.
According to this fact, we say the *Type group* is *Value type group* if it consists of *values_types* or *Class type group* if it consists of *class_types*.

> So, `class Foo<T>` and `class Foo<T1,T2>` are in the same class type group.

> 12.8.16.2 Object creation expressions
>
> The binding-time processing of an *object_creation_expression* of the form new `T(A)`, where `T` is a *type group*, and `A` is an optional *argument_list*, consists of the following steps:
>
> - If `T` is a *type_parameter* and `A` is not present:
>   - If no value type constraint or constructor constraint ([§15.2.5](classes.md#1525-type-parameter-constraints)) has been specified for `T`, a binding-time error occurs.
>   - The result of the *object_creation_expression* is a value of the run-time type that the type parameter has been bound to, namely the result of invoking the default constructor of that type. The run-time type may be a reference type or a value type.
> - Otherwise, the set of candidate constructors for the object construction is constructed in the same way as in the first step in method invocation(proposed version). The initial set is all accesible constructors in *type group* `T`. We clarify that generic constructor matches a constructor of generic type.
> - We remove all constructors of abstract or static *class_type* from the set.
> - If `T` has no type arguments and the set contains types having no type parameters, we run the overload resolution [§12.6.4](expressions.md#1264-overload-resolution) firstly on them.
> - If the set of candidate instance constructors is empty, we run the overload resolution on the remaining candidates.
> - If the set of candidate instance constructors is empty, or if a single best instance constructor cannot be identified, a binding-time error occurs.
> - Otherwise, type containing the best instance constructor is the type of *object_creation_expression*.

#### Variable declaration

> TODO

#### Casting

> TODO

#### Member Lookup

> TODO

## Alternatives

We can choose different char or keywords for determining compiler inferred type argument. Other options consist of `*`, `var`, `?`, `<<whitespace>>`. Although, we think that `_` fits better because the same char is used in deconstruction assignments or patterns.