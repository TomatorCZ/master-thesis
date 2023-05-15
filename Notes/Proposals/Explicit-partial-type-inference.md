# Explicit partial type inference

## Summary

Allow a user to specify only necessary type arguments of

1. Generic method or local function call
2. Generic object and delegate creation
3. Variable declaration of a generic type
4. Cast

by introducing the `_` placeholder to mark type arguments inferred by the compiler.

## Motivation

The current method type inference works as an "all or nothing" principle. 
If the compiler is not able to infer command call type arguments, the user has to specify all of them.
This requirement can be verbose, noisy, and unnecessary in cases where the compiler is able to infer almost all type arguments and need just to specify ambiguous ones.
In these cases, we would like to give the compiler a hint for ambiguous type arguments.
The current source of dependencies, which are used in type inference is restricted to method/function arguments which prevent making the whole type argument list inference in even simple scenarios.
We could use the `_` placeholder for type arguments, which can be inferred from the argument list, and specify the remaining type arguments by ourselves.
The potential additional sources of type information are specified below.

- **Inference by target type** - The current method type inference doesn't use target type for determining type argument in inference resulting in specifying the whole argument list.
- **Inference by `where` clauses** - Utilizing `where` clauses to determine the type argument.
- **Inference by later calls** - Utilizing later method calls to determine the type of the generic object (useful for object creation).
- **Inference by inheritance or implementation** - Utilizing implemented interfaces or inherited class for determining type argument.

Because of restricted type inference, `_` can be used only in restricted scenarios.
However, the potential of this feature with a combination of mentioned type inference improvements can bring more useful cases where to place `_`.
See the following examples, where `_` can be used to hint the compiler type arguments, which could be inferred in case of implemented mentioned inference improvements.

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

Foo<_, int>(new List<int>()) // We can hint the compiler info from `where` clauses
```

```csharp
class Foo<T1, T2> {
    Foo(T1 p) {}
    public void Bar(T2 p) {}
}

var a = Foo<_, int>("str"); // We can hint the compiler type arguments obtained from later calls
a.Bar(1);
```

```csharp
class Foo<TElem, TParam> : IEnumarable<TElem> {
    Foo(TParam p) {}
}

IEnumerable<int> = new Foo<int, _>(42); // we can hint the compiler type argument obtained from interface implementation or class inheritance
```

Even if all of these sources would be used during method inference, there will be still type arguments, which can't be inferred from the context.
We are talking about type arguments, which are used internally in the class or struct and are not exposed to global API.
So It would be still beneficial to have the `_` placeholder.

As you could see in the example, we see a potential to use type inference in constructors.
The motivation behind this can be to specify just the wrapper implementation and let the elements inside the wrapper be inferred.
It would help to make documentation right by the code with the meaning "This segment of code primarily cares about the wrapper".

```csharp
var a = new Wrapper<_>(wrappedElement)
```

Another need where `_` placeholders can be used is specifying the arity of type argument on places, where can be ambiguities like `IEnumerable` vs `IEnumerable<_>`.

Worth to mention other options which could be accomplished in the future regarding default and named type arguments.
Having the `_` placeholder can be used as a shortcut for choosing the right generic overload and to save typing when we use named type parameters.

```csharp
class Foo<T1, T2 = int> {}
class Foo<T1, T2 = int, T3 = string> {}

new Foo<T1: _, T2: string, T3 = _>() // Assuming that T1 can be inferred and T3 is default.
new Foo<_,_> // Choosing Foo<T1, T2> based on the arity
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

Although it is unlikely that it would be added into C#, we would like to investigate what should be done to enable it for a deeper understanding of type inference in Roslyn.

Based on mentioned advantages that `_` placeholder can bring to type inference we feel to add it to C# worthly.

## Scope

Partial type inference can be solved in various ways.
We chose a feature enabling to hint the compiler by specifying ambiguous type arguments and letting the compiler infer the rest.
It aims at cases, where we want just to specify the arity of desired generic method(type) or specify a parameter that is not possible to infer from the context but let the compiler infer the remaining arguments. 

## Design

As we mentioned in the motivation, we see the usage of our proposal in the following places.

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

#### Specification changes

> [8.4.2 Type arguments](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/types.md#842-type-arguments) - Identifying the `_` placehodler as a inferred type argument

```diff
Each argument in a type argument list is simply a type.
+
+ If the type is a `_` identifier, we call it inferred type argument which is valid only in cases, where the type inference is enabled (e.g. Method type inference ([§12.6.3](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#1263-type-inference))). 
```

> [12.8.9.2 Method invocations](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#12892-method-invocations) - Changing list of candidates

```diff
The set of candidate methods for the method invocation is constructed. For each method F associated with the method group M:
    If F is non-generic, F is a candidate when:
        M has no type argument list, and
        F is applicable with respect to A (§12.6.4.2).
+   If F is generic and M includes a type argument list containing at least one inferred type argument(including nested inferred type argument) ([§8.4.2](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/types.md#842-type-arguments)), F is a candidate when:
+       F has the same number of method type parameters as were supplied in the type argument list, and
+       Type inference (§12.6.3) succeeds, inferring a list of type arguments for the call, and
+       Once the type arguments are substituted for the corresponding method type parameters, all constructed types in the parameter list of F satisfy their constraints (§8.4.5), and the parameter list of F is applicable with respect to A (§12.6.4.2).
    If F is generic and M includes a type argument list without any inferred arguments, F is a candidate when:
```

> [12.6.3.1 General](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#12631-General) - Defining type inference including inferred type arguments

```diff
If each supplied argument does not correspond to exactly one parameter in the method (§12.6.2.2), or there is a non-optional parameter with no corresponding argument, then inference immediately fails. Otherwise, assume that the generic method has the following signature:

Tₑ M<X₁...Xᵥ>(T₁ p₁ ... Tₓ pₓ)

-With a method call of the form M(E₁ ...Eₓ) the task of type inference is to find unique type arguments S₁...Sᵥ for each of the type parameters X₁...Xᵥ so that the call M<S₁...Sᵥ>(E₁...Eₓ) becomes valid.
+With a method call of the form `M(E₁ ...Eₓ)` or `M<Y₁...Yᵥ>(E₁ ... Eₓ)` the task of type inference is to find unique type arguments `S₁...Sᵥ` for each of the type parameters `X₁...Xᵥ` so that the call `M<S₁...Sᵥ>(E₁...Eₓ)` becomes valid and in the case of `M<Y₁...Yᵥ>(E₁ ... Eₓ)` type argument `S_k` should by identical with `Y_k` in that way that each inferred type argument `_` contained in `Y_k` can be anything.
+
+ We are introducing type variables P_i, where P_1...P_v are type pararemeters of M and P_{v+1}...P_{v+l} are inferred type arguments ordered in depth-first search of the type argument list.
```

> [12.6.3.2 The first phase](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#12632-the-first-phase) - Adding type arguments to exact bounds of corresponding type parameter

```diff
+For each type argument Y_k add Y_k to the set of exact bounds of type variable V_k

For each of the method arguments `Eᵢ`:
- Rename P_i
+ to P_i refferring extended list of type variables by inferred type variables
```

> [12.6.3.2 The second phase](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#12633-the-second-phase) - Restricting conditions for fixing

```diff
- All unfixed type variables Pᵢ which do not depend on (§12.6.3.6) any Pₑ are fixed (§12.6.3.12).
+ All unfixed type variables Pᵢ which do not depend on (§12.6.3.6) any Pₑ, and all type variables contained in their bounds are fixed, are fixed (§12.6.3.12).
-If no such type variables exist, all unfixed type variables Xᵢ are fixed for which all of the following hold:
-    There is at least one type variable Xₑ that depends on Xᵢ
-    Xᵢ has a non-empty set of bounds
+If no such type variables exist, all unfixed type variables Pᵢ are fixed for which all of the following hold:
+    There is at least one type variable Pₑ that depends on Pᵢ
+    Pᵢ has a non-empty set of bounds
+    All type variables contained in their bounds are fixed
- Rename X_i
+ to P_i refferring extended list of type variables by inferred type variables
```

> [12.6.3.6 Dependence](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#12636-dependence) - Renaming X_i to P_i refferring extended list of type variables by inferred type arguments

```diff
- Rename P_i
+ to P_i refferring extended list of type variables by inferred type variables
```

> [12.6.3.9 Exact inferences](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#12639-exact-inferences) - Propagting the constraints to type variables contained already in bounds

```diff
-If V is one of the unfixed Xᵢ then U is added to the set of exact bounds for Xᵢ.
+If P is one of the unfixed Pᵢ then U is added to the set of exact bounds for Pᵢ.
+ For each upper bound B_u of P_i containing at least one unfixed type variable P_k, an upper-bound inference is made from U to B_u
+ For each exact bound B_e of P_i containing at least one unfixed type variable P_k, an exact-bound inference is made from U to B_e
+ For each lower bound B_l of P_i containing at least one unfixed type variable P_k, an lower-bound inference is made from U to B_l
```

> [12.6.3.10 Lower-bound inferences](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#126310-lower-bound-inferences) - Propagting the constraints to type variables contained already in bounds

```diff
-If V is one of the unfixed Xᵢ then U is added to the set of exact bounds for Xᵢ.
+If P is one of the unfixed Pᵢ then U is added to the set of lower bounds for Pᵢ.
+ For each upper bound B_u of P_i containing at least one unfixed type variable P_k, an lower-bound inference is made from U to B_u
+ For each exact bound B_e of P_i containing at least one unfixed type variable P_k, an lower-bound inference is made from U to B_e
+ For each lower bound B_l of P_i containing at least one unfixed type variable P_k, an lower-bound inference is made from U to B_l
```

> [12.6.3.11 Upper-bound inferences](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#126311-upper-bound-inferences) - Propagting the constraints to type variables contained already in bounds

```diff
-If V is one of the unfixed Xᵢ then U is added to the set of exact bounds for Xᵢ.
+If P is one of the unfixed Pᵢ then U is added to the set of upper bounds for Pᵢ.
+ For each upper bound B_u of P_i containing at least one unfixed type variable P_k, an upper-bound inference is made from U to B_u
+ For each exact bound B_e of P_i containing at least one unfixed type variable P_k, an upper-bound inference is made from U to B_e
+ For each lower bound B_l of P_i containing at least one unfixed type variable P_k, an upper-bound inference is made from U to B_l
```

> [12.6.3.12 Fixing](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#126312-fixing) - Substituting fixed variables in bounds to fixed results.

```diff
- Rename X_i
+ to P_i refferring extended list of type variables by inferred type variables
- The set of candidate types Uₑ starts out as the set of all types in the set of bounds for Xᵢ
+ The set of candidate types Uₑ starts out as the set of all types in the set of bounds for Vᵢ with substituted fixed type variables.
```

> [12.6.5 Compile-time checking of dynamic member invocation](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#1265-compile-time-checking-of-dynamic-member-invocation) - Prohibiting inferred type arguments with dynamic

```diff
+Because type inference when `dynamic` is used is done in run-time, we prohibit using inferred type argument in the type argument list, because the run-time doesn't support it.
Even though overload resolution of a dynamically bound operation takes place at run-time, it is sometimes possible at compile-time to know the list of function members from which an overload will be chosen:
```

> Note: In the current situation, it is impossible that bounds of a type variable contain the type variable itself or even create a cycle. 
> However, it can happen when we add other contraints like `where` clauses.
> How to solve it ?

#### Nested inferred type argument

See the following example.

```csharp
TResult GetResult<TResult>() where TResult : IEnumerable<string> {}

var results = GetResult<List<_>>(); // Just specifying wrapper implementation.
```

It would be most beneficial during working with any generic wrappers, where we are curious about the wrapper implementation, but not which will be inside the wrapper.

This kind of wildcard needs more information about the relation between type arguments to become useful.
However, we can still use it to determine exact type of the wrapper.

```csharp
class B<T1, T2> {}
class A<T1, T2> : B<T1, T2> {}

void M<T1>(T1 p1) {}

M<B<T1, T2>>(new A<int, string>()); // will instatiate T1 = B<int, string>
```

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

Although using nested type arguments will not have significant added value to type inference, they are still valid and it will be useful in the future impovements of the type inference.

#### Object construction

> TODO

#### Variable declaration

> TODO

#### Casting

> TODO

## Alternatives

We can choose different char or keywords for determining compiler inferred type argument. 
Other options consist of `*`, `var`, `?`, `<<whitespace>>`, or no token at all (e.g. `Foo<,>(..,)`). 
Although, we think that `_` fits better because the same char is used in deconstruction assignments or patterns.