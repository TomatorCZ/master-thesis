# Default-type-parameters

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

There could be a third-party library that we can't modify.
In this scenario, we can't use default parameters because we don't have the source code.
We could use inheritance to reduce type parameters, although introducing a new type just for introducing default implementation for some type parameters seems incorrect and even impossible in the case of `sealed` classes.
What we could use instead of creating a new class is an alias representing that type.
However current aliases are very strict to be used for that.
We could extend them to support generic parameters to solve the issue.

## Detailed design

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

We have to deal with common type names and method resolution in other to not introduce breaking changes. Because the proposed improvements are complementary to each other, we describe the rules for using them at the end of the *Detailed design* section. 
You can find a description of the syntax in the *Required changes* section.

**Lowering**

The problem will be how to express it in *CIL* code which doesn't know the default type arguments. 
When we look at the default method argument, we can see the *CIL* have a special attribute `[opt]` for them. 
We can create our custom attribute, which will replace the user's typed `T = value` and decorate the type parameter by him.
In this way, we can keep information about default type arguments in *CIL*. 

**Declaration**

1. In the declaration, a position type parameter must not appear after the default type parameter.
2. Only `struct`, and classes that are not `abstract` and `this` keyword can be used as a default value of a type parameter.

**Type resolution**

We will prioritize types without default type parameters in the resolution in order to not introduce breaking changes. That means.

1. If there is a candidate, which is applicable and doesn't have default type parameters and the remaining candidates contain only generic classes with default type parameters that are also applicable, we will choose the one without default type parameters.
2. Rules for using named arguments are the same as for method parameters.
3. We can use `_` to specify the arity of the type.

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
using_alias_directive
    : 'using' identifier type_parameter_list? '=' namespace_or_type_name ';'
    ;
```
