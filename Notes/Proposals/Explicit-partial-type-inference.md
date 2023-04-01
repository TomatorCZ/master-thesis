# Explicit partial type inference

## Summary

Allow a user to specify only necessary type arguments of

1. a generic method call
2. a generic type declaration

## Motivation

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

Other scenarios contain a specification of all type arguments when declaring generic types. 
We feel, that the way is not ideal because of the unnecessary code that the programmer has to write. 
See the example below. 
We don't have to specify the second type argument, it can be inferred from the constructor. 
However, the first argument cannot be omitted because we want to specify the precision.

```csharp
using System.Collections.Generic;

Calculation<float, object> calculation = new (new List<object>());

public class Calculation<TPrecission, TEntity> {
    public Calculation(List<TEntity> entities) {} 
}
```

## Scope

Partial type inference can be solved in various ways. 
We can inspire from default and named arguments where we don't have to write all arguments. 
Although these options are interesting to investigate, the proposal aims to suggest a way how to specify which positional arguments should be inferred by the compiler. 
This is the reason for the **Explicit** word in the title.

The proposal shouldn't influence the possibility to extend partial type inference in other mentioned ways.

## Detailed design

In our scope, we have to somehow mark inferred type arguments. 
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

## Required changes

### Syntax

1. Method invocation
2. Type declaration

### Method type inference algorithm

1. Prefill type argument list before the `Infer` method

### Declaring type inference algorithm

1. Prefill type argument list before the `Infer` method.

## Alternatives

We can choose different char or keywords for determining compiler inferred type argument. Other options consist of `*`, `var`, `?`, `<<whitespace>>`. Although, we think that `_` fits better because the same char is used in deconstruction assignments or patterns.