# Partial type inference

## Summary

Allow user to specify just necessary type arguments of

1. generic method call
2. generic type declaration

## Motivation

Current generic method type inference situation works as "all or nothing" principle. 
If compiler is not able to infer command call type arguments, an user has to specify all of them. 
This requirement can be noisy and unnecessary in scenarios where some type arguments can be inferred by compiler based on other dependencies.

We can take an example from `System.Linq` library. 
When we use `Select` method, the first argument can be inferred from target. 
However, the second argument hasn't be obvious from context. 
We can see it in the example, where we want to specify the precission. 
The better way would be to skip obvious type parameters (`int` here) and specify just ambigious ones(`float` here).

```csharp
using System.Collections.Generic;
using System.Linq;

var scores = new List<int>() {1,2,3};
// Error: IEnumerable<float> preciseScores = scores.Select(i => i);
IEnumerable<float> preciseScores = scores.Select<int, float>(i => i); // OK
```

Other scenarios contain specification of all type arguments when declaring generic types.
We feel, that the way is not ideal because of unnecesarry code which programmer has to write. 
See the example below. 
We don't have to specify the second type argument, it can be inferred from constructor. 
However we want to specify the precission.

```csharp
using System.Collections.Generic;

Calculation<float, object> calculation = new (new List<object>());

public class Calculation<TPrecission, TEntity> {
    public Calculation(List<TEntity> entities) {} 
}
```

## Scope

Partial type inference can be solved by various ways. 
We can inspire from default and named arguments where we don't have to write all arguments. Althought these options are interesting to investigate, the proposal aims to suggest a way how to specify which positional arguments should be inferred by compiler.

The proposal scope shoudn't influence adding other mentioned ways for partial type inference.

## Detailed design

In our scope we have to somehow mark inferred type arguments. 
The most natural aproach would be to replace the type arguments by some keyword. 
Let's pick `_` for it for now and show it in the example.

```csharp
void foo<T1, T2, T3>(T2 arg) {}

foo<int, _, int>("string"); // _ is marked as "inferred by compiler"
```

 One can suggest to don't mark it and just to leave it on compiler. 
 However, it would bring ambigitious between which arguments we want to be inferred. 

```csharp
void foo(int arg1, int arg2) {}
void foo<T1>(T1 arg1, int arg2) {}
void foo<T1, T2>(T1 arg1, T2 arg2) {}

foo(1,2); // We don't know which overload to choose
foo<>(1,2); // Still, it is not clear which generic overload should be choose.
```

Because of mentioned issues, we believe that marking each type argument, what the compiler will infer, is right way how to do it.

The next level of marking the type argument to be inferred by compiler is to use wildcards. 
See the following example.

```csharp
TResult GetResult<TResult>() where TResult : IEnumerable<string> {}

var results = GetResult<List<_>>(); // Just specifying wrapper implementation.
```

It would be mostly benefitial during working with any generic wrappers, where we are curious about the wrapper implementation, but not which will by inside the wrapper.

This kind of wildcards need more information about relation between type arguments to become useful.
The more infromation can be extracted from generic type constrains, which can bring also more inference power to simple `_` infereded types.
However, we have to be careful where we use the constrains information, because it can bring breaking change.
See the following code.

```csharp
void M(object) {}
void M<T, U>(T t) where T : IEnumerable<U> {}

M("foo");
```

Beacuse of this breaking change, we can use the constains only with `_` inferred types, which is not presented in current language version so it doesn't cause breaking change.
New usage of it can look like this.

```csharp
void M(object) {} // 1.
void M<T, U>(U t) where T : IEnumerable<U> {} // 2.

M<_, _>("foo"); // calls 2.
M("foo"); // calls 1.
```

One can feel that `M<_,_>(...)` should be resolved in the same manner as `M(...)`. 
However, we feel that `_` could be considered as a better inference.
In scenarious, when there are many type parameters and each of them could be resolved by using constains information, we can use shortcut.

```csharp
M<>("foo"); // Infer all arguments
```

> More examples where it would be benefical.

> Implicit attribute ??


## Required changes

### Syntax

1. Method invocation

```
invocation
    : name ("<" type_arg_list_with_inference  ">")? "(" arg_list ")"
    ;
type_arg_list_with_inference
    : (name | "_")+
```

2. Type declaretion

```
type_declaration
    : "var"
    : common...
    : special (_)
    ;
```

### Method type inference algorithm

1. Prefill arg list before `Infer`
2. Take generic constraints into account.

### Declaring type inference algorithm

1. Prefill arg list before `Infer`
2. Take generic constraints into account.

## Alternatives

We can choose different char or keyword for determining compiler inferred type argument. Other options consists of `*`, `var`, `?`, `<<whitespace>>`. Although, we think that `_` fits better because the same char is used in deconstruction assignments or patterns.