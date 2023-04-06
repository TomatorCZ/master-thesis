# Explicit and implicit partial type inference

## Summary

Allow a user to specify only necessary type arguments of

1. Generic method or function call
2. Generic type

by introducing

1. `_` determining type argument inferred by compiler
2. default type arguments
3. named type arguments

## Motivation

We think that the mentioned improvements make the scenarios below easier for programmers in the way to not write unnecessary type arguments which can be inferred from the context.

- Declaration of `class`, `struct`, `interface`
- Declaration of variables
- Calling generic methods and constructors

There are scenarios when implementing(or inheriting) `interface`(or `class`) requires specifying many type arguments.
Since we have generics, it is common for frameworks to use them to offer base implementation of an algorithm that is specialized in the user's code.
Because these frameworks try to provide a solution for all cases, the contracts can sometimes contain many type arguments which have to be filled in by the user during implementation.
We can take an example from `EntityFramework` providing `IdentityDbContext` used for identity.

```csharp
public abstract class IdentityDbContext<TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken> : IdentityUserContext<TUser,TKey,TUserClaim,TUserLogin,TUserToken> 
where TUser : IdentityUser<TKey>
where TRole : IdentityRole<TKey> 
where TKey : IEquatable<TKey> 
where TUserClaim : IdentityUserClaim<TKey> 
where TUserRole : IdentityUserRole<TKey> 
where TUserLogin : IdentityUserLogin<TKey>
where TRoleClaim : IdentityRoleClaim<TKey> 
where TUserToken : IdentityUserToken<TKey>
```

The user has to fill in all type arguments even if some of them are usually the same.
A good idea would be to introduce default type arguments to solve common situations.
It would have two advantages. 
The user doesn't have to care about default implementation until he needs to customize it.
The user doesn't have to fill in default implementation which will give better readability of code.  

In the example, we can see a potential second improvement.
The clarity of the call site decreases when we have many parameters.
Taking the example, it is hard to distinguish which type argument is for `TUserRole` and `TRole`, because it depends only on positions that are defined in the class declaration.
Introducing named type arguments could help with matching type arguments with type parameters and it is a complement of default arguments. 

Other scenarios contain a specification of all type arguments when declaring generic types. 
We feel, that the way is not ideal because of the unnecessary code that the programmer has to write. 
See the example below. 
We don't have to specify the second type argument, it can be inferred from the constructor. 
However, the first argument cannot be omitted because we want to specify the precision.

```csharp
using System.Collections.Generic;

Calculation<float, object> calculation = new (new List<object>());
//or
var calculation = new Calculation<float, object>(new List<object>());

public class Calculation<TPrecission, TEntity> {
    public Calculation(List<TEntity> entities) {} 
}
```

The current generic method type inference situation works as an "all or nothing" principle. 
If the compiler is not able to infer command call type arguments, a user has to specify all of them. 
This requirement can be noisy and unnecessary in scenarios where some type arguments can be inferred by the compiler based on other dependencies.

We can take an example from the `System.Linq` library. 
When we use the `Select` method, the first argument can be inferred from a target. 
However, the second argument doesn't have to be obvious from the context. 
We can see it in the example, where we want to specify the precision. 
The better way would be to skip obvious type parameters (`int` here) and specify just ambiguous ones(`float` here).

```csharp
using System.Collections.Generic;
using System.Linq;

var scores = new List<int>() {1,2,3};
// Error: IEnumerable<float> preciseScores = scores.Select(i => i);
IEnumerable<float> preciseScores = scores.Select<int, float>(i => i); // OK
```


## Scope

Partial type inference can be solved in various ways. We can inspire from default and named arguments where we don't have to write all arguments. We will call it **Implicit partial type inference**. 

Also, there are situations, where we want just to specify the arity of desired generic parameter or specify a parameter that is not possible to infer from the context but let the compiler infer the remaining arguments. This part aims to suggest a way how to specify which positional arguments should be inferred by the compiler. We will call it **Explicit partial type inference**.

## DetailedDesign

### Implicit partial type inference

Implicit partial type inference is fully inspired by default and named arguments of argument list of function members with slight modification including bringing a new concept of `this` keyword and combination with *Explicit partial type inference*.

#### Default type parameters

We can use defaults in a similar way as in method declaration.

```csharp
class Foo<T1 = T1Default, T2 = T2Default> {}

class T1Default {}

class T2Default {}
...
var foo = new Foo(){}
```

It can be worthly to be able to refer ancestor implementin or inheriting the `interface` or `class` in scenarios, where the predecesor works with the type of his ancestor.
See the example below.

```csharp
interface IEquitable<T> {} // We would like to `T` be an implementor in default.

class X : IEquitable<X> {}
```

We can use `this` keyword to express the intention on the place for default value.

We have to deal with common type names and method resolution in other to not introduce breaking changes. Because the proposed improvements are complementary to each other, we describe the rules for using them at the end of the *Detailed design* section. You can find a description of the syntax in the *Required changes* section.

**Lowering**

The problem will be how to express it in *CIL* code which doesn't know the default type arguments. 
When we look at the default method argument, we can see the *CIL* have a special attribute `[opt]` for them. 
We can create our custom attribute, which will replace the user's typed `T = value` and decorate the type parameter by him.
In this way, we can keep information about default type arguments in *CIL*. 

#### Named type parameters

We will take an example from named method parameters again and introduce them in type parameters.

```csharp
class Foo<T1, T2> {}

class Bar : Foo<T2:int, T1:string> {}
```

**Lowering**

We have to lower named type parameters to be able to compile the into *CIL*, which doesn't know them.
It can be done by reordering them to accordingly match positional parameters.
The same trick is used for named method type parameters.

### Explicit partial type inference

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

The next level of marking the type argument to be inferred by the compiler is wildcards usage. 
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

### Rules

**Declaration**

1. In the declaration, a position type parameter must not appear after the default type parameter.
2. Only `struct`, and classes that are not `abstract` and `this` keyword can be used as a default value of a type parameter.

**Type resolution**

We will prioritize types without default type parameters in the resolution in order to not introduce breaking changes. That means.

1. If there is a candidate, which is applicable and doesn't have default type parameters and the remaining candidates contain only generic classes with default type parameters that are also applicable, we will choose the one without default type parameters.
2. Rules for using named arguments are the same as for method parameters.
3. We can use `_` to specify the arity of the type.

## Required changes

### Syntax

We have to change grammar allowing the user to use mentioned constructs.

**Declaration**

```
type_parameters
    : attributes? type_parameter
    | type_parameters ',' attributes? type_parameter
    ;

type_parameter
    : attributes? type identifier default_argument?
    ;

default_argument
    : '=' (type | 'this')
    ;
```

**Usage**

```
type_argument_list
    : '<' type_arguments '>'
    ;

type_arguments
    : type_argument (',' type_argument)*
    ;   

type_argument
    : type_argument_name? type_argument_value
    ;

type_argument_name
    : identifier ':'
    ;

type_argument_value
    : type | '_'
    ;
```

### Inference algorithm

>TODO

## Alternatives

We can choose different char or keywords for determining compiler inferred type argument. Other options consist of `*`, `var`, `?`, `<<whitespace>>`. Although, we think that `_` fits better because the same char is used in deconstruction assignments or patterns.