# Partial type inference

* [x] Proposed
* [ ] Prototype
* [ ] Implementation
* [ ] Specification

> Note: This proposal was created because of championed [Partial type inference](https://github.com/dotnet/csharplang/issues/1349). It is a continuation of the proposed first version published in [csharplang/discussions](https://github.com/dotnet/csharplang/discussions/7286)

## Summary
[summary]: #summary

Partial type inference introduces a syntax skipping obvious type arguments in the argument list of

1. *invocation_expression*
2. *object_creation_expresssion*

and allowing to specify just ambiguous ones.

It also improves the type inference in the case of *object_creation_expression* by leveraging type bounds obtained from the target, *object_or_collection_initializer*, and *type_parameter_constraints_clauses*. 

Besides the changes described above, the proposal mentions further interactions and possibilities to extend the partial type inference.

## Motivation
[motivation]: #motivation

- The current method type inference works as an "all or nothing" principle.
If the compiler is not able to infer command call type arguments, the user has to specify all of them.
This requirement can be verbose, noisy, and unnecessary in cases where the compiler can infer almost all type arguments and need just to specify ambiguous ones.
- The need to hint types to the compiler is influenced by the strength of the type inference which is not as advanced as in other statically-typed languages like Rust or Haskell.
However, we can't just change the current behavior of the type inference because it would introduce breaking changes.
What we can do is to introduce improved type inference in places, where it was not before like *object_creation_expression*.
It is a nice chance to push the type inference to the next level without introducing breaking changes.
And then wait for the time, when C# would be ready to introduce breaking changes without any major disadvantages.
- Because there exist types containing many type parameters (especially in frameworks focusing on databases and web), it would be great to add type inference of constructors to save unnecessary specifying the type arguments.

No matter how the partial type inference would work, we should be careful about the following things.

- **Convenience** - We want an easy and intuitive syntax that we can skip the obvious type arguments.
- **Performance** - Type inference is a complicated problem when we introduce subtyping and overloading in a type system.
Although it can be done, the computation can take exponential time which we don't want.
So it has to be restricted to cases, where the problem can be solved effectively but it still has practical usage.
- **IDE** - Improvement of the type inference can complicate IDE hints during coding. 
We should give the user clear and not overwhelming errors when there will be an error and try to provide info that helps him to fix it.
- **Extensions** - We don't want to make this change blocker for another potential feature in the future. 
So will want to look ahead to other potential directions, which can be done after this feature.

## Detailed design
[design]: #detailed-design

### Grammar

The following changes are made in [tokens](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/lexical-structure.md#64-tokens) located in the [grammar](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/lexical-structure.md#62-grammars) section.

> [Identifiers](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/lexical-structure.md#643-identifiers)

- The semantics of an identifier named `_` depends on the context in which it appears:
  - It can denote a named program element, such as a variable, class, or method, or
  - It can denote a discard ([§9.2.9.1](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/variables.md#9291-discards)).
  - **It can denote an inferred type argument avoiding specifying type arguments which can be inferred by the compiler.**

> [Keywords](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/lexical-structure.md#644-keywords)

* A ***contextual keyword*** is an identifier-like sequence of characters that has special meaning in certain contexts, but is not reserved, and can be used as an identifier outside of those contexts as well as when prefaced by the `@` character.

  ```diff
  contextual_keyword
      : 'add'    | 'alias'      | 'ascending' | 'async'     | 'await'
      | 'by'     | 'descending' | 'dynamic'   | 'equals'    | 'from'
      | 'get'    | 'global'     | 'group'     | 'into'      | 'join'
      | 'let'    | 'nameof'     | 'on'        | 'orderby'   | 'partial'
      | 'remove' | 'select'     | 'set'       | 'unmanaged' | 'value'
  +   | 'var'    | 'when'       | 'where'     | 'yield'     | '_'
  -   | 'var'    | 'when'       | 'where'     | 'yield'
    ;
  ```

### Type arguments

* We change the meaning of the content of *type_argument_list* in two contexts.
  * [Constructed types](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/types.md#84-constructed-types) occuring in [*object_creation_expression*](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#128162-object-creation-expressions)
  * Constructed types and type arguments occuring in method [invocation](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#12892-method-invocations)

* ***inferred_type_argument*** represents an unknown type, which will be resolved during type inference. 

* `_` identifier is considered to represent *inferred_type_argument* when:
  * It occurs in *type_argument_list* of a method group during method invocation.
  * It occurs in *type_argument_list* of a type in *object_creation_expression*.
  * It occurs as an arbitrary nested identifier in the expressions mentioned above.
  > Example
  >
  > ```csharp
  > F<_, int>(...); // _ represents an inferred type argument.
  > new C<_, int>(...); // _ represents an inferred type argument.
  > F<C<_>, int>(...); // _ represents an inferred type argument.
  > new C<C<_>, int>(...); // _ represents an inferred type argument.
  > C<_> temp = ...; // _ doesn't represent an inferred type argument.
  > new _() // _ doesn't represent an inferred type argument.
  > ```   

* A method group and type are said to be *partial_inferred* if it contains at least one *inferred_type_argument*. 

* A type is said to be *generic_inferred* when all the following hold:
  * It has an empty *type_argument_list*.
  * It occurs as a *type* of *object_creation_expression*.
  > Example
  >
  > ```csharp
  > new C<>() // C is generic_inferred.
  > new C<G<>>() // C nor G are generic_inferred.
  > F<>() // F isn't generic_inferred.
  > ```

### Namespace and type names

Determining the [meaning](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/basic-concepts.md#781-general) of a *namespace_or_type_name* is changed as follow.

* If a type is a *generic_inferred*, then we resolve the identifier in the same manner except ignoring the arity of type parameters (Types of arity 0 is ignored). 
If there is an ambiguity in the current scope, a compilation-time error occurs.
  > Example
  >
  > ```csharp
  > class P1
  > {
  >     void M() 
  >     {
  >         new C1<>(); // Refers generic_inferred type C1<T>
  >         new C2<>(); // Refers generic_inferred type C2<T1,T2>
  >     }
  >     class C1<T> {}
  >     class C2<T1, T2> {}
  > }
  > class P2
  > {
  >     void M() 
  >     {
  >         new C1<>(); // Compile-time error occurs because of ambiguity between C1<T> and C1<T1, T2>
  >     }
  >     class C1<T> {}
  >     class C1<T1, T2> {}
  > }
  > ``` 

### Method invocations

The binding-time processing of a [method invocation](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#12892-method-invocations) of the form `M(A)`, where `M` is a method group (possibly including a *type_argument_list*), and `A` is an optional *argument_list* is changed in the following way.

The initial set of candidate methods for is changed by adding new condition.

- If `F` is non-generic, `F` is a candidate when:
  - `M` has no type argument list, and
  - `F` is applicable with respect to `A` ([§12.6.4.2](expressions.md#12642-applicable-function-member)).
- If `F` is generic and `M` has no type argument list, `F` is a candidate when:
  - Type inference ([§12.6.3](expressions.md#1263-type-inference)) succeeds, inferring a list of type arguments for the call, and
  - Once the inferred type arguments are substituted for the corresponding method type parameters, all constructed types in the parameter list of `F` satisfy their constraints ([§8.4.5](types.md#845-satisfying-constraints)), and the parameter list of `F` is applicable with respect to `A` ([§12.6.4.2](expressions.md#12642-applicable-function-member))
- If `F` is generic and `M` has type argument list containing at least one *inferred_type_argument*, `F` is a candidate when:
  - Type inference ([§12.6.3](expressions.md#1263-type-inference)) succeeds, inferring a list of *inferred_type_arguments* for the call, and
  - Once the *inferred_type_arguments* are inferred and together with remaining type arguments are substituted for the corresponding method type parameters, all constructed types in the parameter list of `F` satisfy their constraints ([§8.4.5](types.md#845-satisfying-constraints)), and the parameter list of `F` is applicable with respect to `A` ([§12.6.4.2](expressions.md#12642-applicable-function-member))
- If `F` is generic and `M` includes a type argument list, `F` is a candidate when:
  - `F` has the same number of method type parameters as were supplied in the type argument list, and
  - Once the type arguments are substituted for the corresponding method type parameters, all constructed types in the parameter list of `F` satisfy their constraints ([§8.4.5](types.md#845-satisfying-constraints)), and the parameter list of `F` is applicable with respect to `A` ([§12.6.4.2](expressions.md#12642-applicable-function-member)).

### Object creation expressions

The binding-time processing of an [*object_creation_expression*](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#128162-object-creation-expressions) of the form new `T(A)`, where `T` is a *class_type*, or a *value_type*, and `A` is an optional *argument_list*, is changed in the following way.

> Note: Type inference of constructor is described later in the type inference section.

The binding-time processing of an *object_creation_expression* of the form new `T(A)`, where `T` is a *class_type*, or a *value_type*, and `A` is an optional *argument_list*, consists of the following steps:

- If `T` is a *value_type* and `A` is not present:
  - The *object_creation_expression* is a default constructor invocation. 
    - If the type is *generic_inferred* or *partially_inferred*, type inference of the default constructor occurs to determine the type arguments. If it succeeded, construct the type using inferred type arguments. If it failed and there is no chance to get the target type now or later, the binding-time error occurs. Otherwise, repeat the binding when the target type will be determined and add it to the inputs of type inference.
    - If the type inference above succeeded or the type is not inferred, the result of the *object_creation_expression* is a value of (constructed) type `T`, namely the default value for `T` as defined in §8.3.3.
- Otherwise, if `T` is a *type_parameter* and `A` is not present:
  - If no value type constraint or constructor constraint (§15.2.5) has been specified for `T`, a binding-time error occurs.
  - The result of the *object_creation_expression* is a value of the run-time type that the type parameter has been bound to, namely the result of invoking the default constructor of that type. The run-time type may be a reference type or a value type.
- Otherwise, if `T` is a *class_type* or a *struct_type*:
  - If `T` is an abstract or static *class_type*, a compile-time error occurs.
  - The instance constructor to invoke is determined using the overload resolution rules of §12.6.4. The set of candidate instance constructors is determined as follows:
    - `T` is not inferrred (*generic_inferred* or *partially_inferred*), the constructor is accessible in `T`, and is applicable with respect to `A` (§12.6.4.2). 
    - If `T` is *generic_constructed* or *partially_constructed* and the constructor is accessible in `T`, type inference of the constructor is performed. Once the *inferred_type_arguments* are inferred and together with the remaining type arguments are substituted for the corresponding type parameters, all constructed types in the parameter list of the constructor satisfy their constraints, and the parameter list of the constructor is applicable with respect to `A` (§12.6.4.2).
  - A binding-time error occurs when:
    - The set of candidate instance constructors is empty, or if a single best instance constructor cannot be identified, and there is no chance to know the target type now or later.
  - If the set of candidate instance constructors is still empty, or if a single best instance constructor cannot be identified, repeat the binding of the *object_creation_expression* to the time, when target type will be known and add it to inputs of type inference.
  - The result of the *object_creation_expression* is a value of type `T`, namely the value produced by invoking the instance constructor determined in the two steps above.
  - Otherwise, the *object_creation_expression* is invalid, and a binding-time error occurs.

### Type inference

We change the [type inference](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#1263-type-inference) as follows.

* Type inference for generic method invocation is performed when the invocation:
  * Doesn't have a *type_argument_list*.
  * The type argument list contains at least one *inferred_type_argument*.
  > Example
  > 
  > ```csharp
  > M(...); // Type inference is invoked.
  > M<_, string>(...); // Type inference is invoked.
  > M<List<_>, string>(...); // Type inference is invoked.
  > ```

* **Type inference for constructors** is performed when the generic type of *object_creation_expression*:
  * Has a diamond operator.
  * Its *type_argument_list* contains at least one *inferred_type_argument*.
  > Example
  >
  > ```csharp
  > new C<>(...); // Type inference is invoked.
  > new C<_, string>(...); // Type inference is invoked.
  > new C<List<_>, string>(...); // Type inference is invoked.
  > ```

* When the method invocation contains a type argument list containing inferred type argument, the input for type inference is extended as follows:
  * We replace each `_` identifier with a new type variable `X`.
  * We perform *shape inference* from each type argument to the corresponding type parameter.

* Inputs for **constructor type inference** are constructed as follows:
  * If the inferred type contains a nonempty *type_argument_list*, we process it in the same manner as in the method invocation.
  * If the target type should be used based on the expression binding, perform *upper-bound inference* from it to the type containing the constructor
  * If the expression contains an *object_initializer_list*, for each *initializer_element* of the list perform *lower-bound inference* from the type of the element to the type of *initializer_target*. If the binding of the element fails, skip it.
  * If the expression contains *where* clauses defining type constraints of type parameters of the type containing constructor, for each constraint not representing *constructor* constrain, *reference type constraint*, *value type constraint* and *unmanaged type constraint* perform *lower-bound inference* from the constraint to the corresponding type parameter.
  * If the expression contains a *collection_initializer_list* and the type doesn't have overloads of the `Add` method, for each *initializer_element* of the list perform *lower-bound inference* from the types of the elements contained in the *initializer_element* to the types of the method's parameters. If the binding of any element fails, skip it.  
  * If the expression contains a *collection_initializer_list* using an indexer, use the indexer defined in the type and perform *lower_bound_inference* from the types in *initializer_element* to types of matching parameters of the indexer. 

* Arguments binding
  * It can happen that an argument of an expression will be *object_creation_expression*, which needs a target type to be successful binded. 
  * In these situations, we behave like the type of the argument is unknown and bind it when we will know the target type.
  * We treat it in the same manner as an unconverted *new()* operator.

#### Type inference algorithm change

* Shape dependence
  * An *unfixed* type variable `Xᵢ` *shape-depends directly on* an *unfixed* type variable `Xₑ` if `Xₑ` represents *inferred_type_argument* and it is contained in *shape bound* of the type variable `Xᵢ`.
  * `Xₑ` *shape-depends on* `Xᵢ` if `Xₑ` *shape-depends directly on* `Xᵢ` or if `Xᵢ` *shape-depends directly on* `Xᵥ` and `Xᵥ` *shape-depends on* `Xₑ`. Thus “*shape-depends on*” is the transitive but not reflexive closure of “*shape-depends directly on*”.
* Type dependence
  * An *unfixed* type variable `Xᵢ` *type-depends directly on* an *unfixed* type variable `Xₑ` if `Xₑ` occurs in any bound of type variable `Xᵢ`.
  * `Xₑ` *type-depends on* `Xᵢ` if `Xₑ` *type-depends directly on* `Xᵢ` or if `Xᵢ` *type-depends directly on* `Xᵥ` and `Xᵥ` *type-depends on* `Xₑ`. Thus “*type-depends on*” is the transitive but not reflexive closure of “*type-depends directly on*”.
* Shape-bound inference
  * A *shape-bound* inference from a type `U` to a type `V` is made as follows:
    * If `V` is one of the *unfixed* `Xᵢ` then `U` is shape-bound of `V`.
    * When new bound `U` is added to the set of lower-bounds of `V`:
      * We perform *lower-bound* inference from `U` to all lower-bounds of `V`, which contains an unfixed type variable
      * We perform *exact* inference from `U` to all exact-bounds of `V`, which contains an unfixed type variable.
      * We perform *upper-bound* inference from `U` to all upper-bounds of `V`, which contains an unfixed type variable.
      *  We perform *lower-bound* inference from all lower-bounds of `V` to `U` if `U` contains an unfixed type variable.
      *  We perform *exact* inference from all exact-bounds of `V` to `U` if `U` contains unfixed type variable.
      *  We perform *upper-type* inference from all upper-bounds of `V` to `U` if `U` contains an unfixed type variable.
    * Otherwise, on inferences are made
* Lower-bound inference
  * When new bound `U` is added to the set of lower-bounds of `V`:
    *  We perform *lower-bound* inference from `U` to shape-bound of `V` , if has any and the shape-bound contains an unfixed type variable.
    * We perform *upper-bound* inference from shape-bound of `V` to `U`, if `V` has a shape-bound and `U` contains an unfixed type variable.
    * We perform *exact* inference from `U` to all lower-bounds of `V`, which contains an unfixed type variable
    * We perform *lower-bound* inference from `U` to all exact-bounds and upper-bounds of `V`, which contains an unfixed type variable.
    *  We perform *exact* inference from all lower-bounds of `V` to `U` if `U` contains an unfixed type variable
    *  We perform *upper-bound* type inference from all exact-bounds and upper-bounds of `V` to `U` if `U` contains unfixed type variable.
* Upper-bound inference
  * When new bound `U` is added to the set of upper-bounds of `V`:
    *  We perform *upper-bound* inference from `U` to shape-bound of `V` , if has any and the shape-bound contains an unfixed type variable.
    * We perform *lower-bound* inference from shape-bound of `V` to `U`, if `V` has a shape-bound and `U` contains an unfixed type variable.
    * We perform *exact* inference from `U` to all upper-bounds of `V`, which contains an unfixed type variable
    * We perform *upper-bound* inference from `U` to all exact-bounds and lower-bounds of `V`, which contains an unfixed type variable.
    *  We perform *exact* inference from all upper-bounds of `V` to `U` if `U` contains an unfixed type variable
    *  We perform *lower-bound* type inference from all exact-bounds and lower-bounds of `V` to `U` if `U` contains unfixed type variable.
* Exact inference
  * When new bound `U` is added to the set of lower-bounds of `V`:
    *  We perform *exact-bound* inference from `U` to shape-bound of `V` , if has any and the shape-bound contains an unfixed type variable.
    * We perform *exact* inference from shape-bound of `V` to `U`, if `V` has a shape-bound and `U` contains an unfixed type variable.
    * We perform *exact* inference from `U` to all exact-bounds of `V`, which contains an unfixed type variable
    * We perform *lower-bound* inference from `U` to all lower-bounds of `V`, which contains an unfixed type variable
    * We perform *upper-bound* inference from `U` to all upper-bounds of `V`, which contains an unfixed type variable
    * We perform *exact* inference from all exact-bounds of `V` to `U`, which contains an unfixed type variable
    * We perform *upper-bound* inference from all lower-bounds of `V` to `U`, which contains an unfixed type variable
    * We perform *lower-bound* inference from all upper-bounds of `V` to `U`, which contains an unfixed type variable
  
* Second phase
  * Firstly, All *unfixed* type variables `Xᵢ` which do not *depend on* ([§12.6.3.6](expressions.md#12636-dependence)), *shape-depend on*, and *type-depend on* any `Xₑ` are fixed ([§12.6.3.12](expressions.md#126312-fixing)).
  * If no such type variables exist, all *unfixed* type variables `Xᵢ` are *fixed* for which all of the following hold:
    * There is at least one type variable `Xₑ` that *depends on*, *shape-depends on*, or *type-depends on* `Xᵢ`
    * There is no type variable `Xₑ` on which `Xᵢ` *shape-depends on*.
    * `Xᵢ` has a non-empty set of bounds and has at least on bound which doesn't contain any *unfixed* type variable.
  * Otherwise continue as in standard
    
* Fixing
  * An *unfixed* type variable `Xᵢ` with a set of bounds is *fixed* as follows:
    * If the type variable has a shape bound, check the type has no conflicts with other bounds of that type variable in the same way as in the standard. It it has no conflicts, the type variable is *fixed* to that type. Otherwise type inference failed.
    * Otherwise, fix it as standard says. 

#### Type inference for constructor

> Note: Complexity
>
> Because performing type inference can even take exponential time, the restriction was made above to avoid it. 
> It regards to permit only one method `Add` in the collections and binding of elements in the constructors where before the overload resultion we bind all *object_creation_expressions* without target info and then in case of overload resulution success and some of these elements failed in the binding, we try to bind it again with already known target type information.

### Compile-time checking of dynamic member invocation

We change the [compile-time checking](https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/expressions.md#1265-compile-time-checking-of-dynamic-member-invocation) in order to be useful during partial type inferece.

- First, if `F` is a generic method and type arguments were provided, then those, that aren't *inferred_type_argument* are substituted for the type parameters in the parameter list. However, if type arguments were not provided, no such substitution happens.
- Then, any parameter whose type is open (i.e., contains a type parameter; see [§8.4.3](types.md#843-open-and-closed-types)) is elided, along with its corresponding parameter(s).

### Nullability

We can use examination mark `?` to say that inferred type argument should be nullable type (e.g. `F<_?>(...)`).

## Drawbacks
[drawbacks]: #drawbacks

Why should we *not* do this?

## Alternatives
[alternatives]: #alternatives

What other designs have been considered? What is the impact of not doing this?

## Unresolved questions
[unresolved]: #unresolved-questions

* Type inference for arrays

  In a similar way as we propose partial type inference in method type inference. 
  It can be used in *array_creation_expression* as well(e.g. `new C<_>[]{...}`). 
  However, It has the following complication.
  To avoid a breaking change, the type inference has to be as powerful as in method type inference. There is a question if it is still as valueble as in cases with methods.

* Type inference of delegates

  We can do the same thing for `delegate_creation_expression`. However, these expressions seems to be used rarely, so is it valuable to add the type inference for them as well ?

* Type inference for local variables

  Sometimes `var` keyword as a variable declaration is not sufficient.
  We would like to be able to specify more the type information about variable but still have some implementation details hidden.
  With the `_` placeholder we would be able to specify more the shape of the variable avoiding unnecessary specification of type arguments.

  ```csharp
  Wrapper<_> wrapper = ... // I get an wrapper, which I'm interested in, but I don't care about the type arguments, because I don't need them in my code.
  wrapper.DoSomething();
  ```

* Type inference for casting

  This can be useful with combination with prepering collection literals.

  ```csharp
  var temp = (Span<_>)[1,2,3];
  ```

* Is there a better choice for choosing the placeholder for inferred type argument ?

    Potentional resolution: My choice contained in the [detailed design](#detailed-design) is based on the following.

<details>

We base our choice on the usages specified below.

1. Type argument list of generic method call (e.g. `Foo<T1, T2>(...)`)
2. Type argument list of type creation (e.g. `new Bar<T1, T2>(...)`)
3. Type argument list of local variable (e.g. `Bar<T1, T2> temp = ...`)
4. Expressing array type (e.g. `T1[]`)
5. Expressing inferred type alone `T1` in local variable

**Diamond operator**

1. In the case of generic method calls it doesn't much make sense since method type inference is enabled by default without using angle brackets.

```csharp
Foo<>(arg1, arg2, arg3); // Doesn't bring us any additional info
```

2. There is an advantage. It can turn on the type inference. However, it would complicate overload resolution because we would have to search for every generic type of the same name no matter what arity. But could make a restriction. Usually, there is not more than one generic type with the same name. So when there will be just one type of that name, we can turn the inference on.

```csharp
new Bar<>(); // Many constructors which we have to investigate for applicability
new Baz<>(); // Its OK, we know what set of constructors to investigate.

class Bar { ... }
class Bar<T1> { ... }
class Bar<T1, T2> { ... }

class Baz<T1,T2> {...}
```

3. It could make sense to specify just a wrapper of some type that gives us general API that doesn't involve its type arguments. It would say that the part of the code just cares about the wrapper. However, we think that it doesn't give us much freedom because type arguments usually appear in public API and only a few of them are for internal use. 

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

1. It is able to specify the arity of the generic method. However, it seems to be messy when it is used in generic methods with many generic type parameters. Also, it already has its own meaning of expressing open generic type.

```csharp
Foo<,string,List<>,>(arg1, arg2, arg3);
```

1. The same reasoning as above.

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

5. It looks like CSharp would not be a statically-typed language, clashed with `var` and probably introduce many implementation problems in the parser.

```csharp
temp = ...
```

**_ seperated by commas**

1. It specifies the arity of the generic method. It explicitly says that we want to infer this type argument. It seems to be less messy.

```csharp
Foo<_, string, List<_>, _>(arg1, arg2, arg3);
```

2. The same reasons as above.

```csharp
new Bar<_, string, List<_>, _>(arg1, arg2, arg3);
```

3. The same reasons as above.

```csharp
Bar<_, string, List<_>, _>(arg1, arg2);
```

4. Looks quite OK.

```csharp
_[] temp = ...
```

5. Clashes with `var` and seems to be wierd.

```csharp
_ temp = ...
```

**var seperated by commas**

1. More keystrokes. It starts to raise the question if it brings the advantage of saving keystrokes.

```csharp
Foo<var, string, List<var>, var>(arg1, arg2, arg3);
```

2. The same reasons as above

```csharp
new Bar<var, string, List<var>, var>(arg1, arg2, arg3);
```

3. The same reasons as above.

```csharp
Bar<var, string, List<var>, var>(arg1, arg2);
```

1. Looks OK.

```csharp
var[] temp = ...
```

5. State of the art.

```csharp
var temp = ...
```

**Something else seperated by commas**

Doesn't make a lot of sense because it needs to assign new meaning to that character in comparison with `_`, `var`, `<>`, `<,,,>`. 
Asterisk `*` can be considered, however, it can remind a pointer.  

**Conslusion**

I prefer `_` character with enabling `<>` operator in the case of constructor inference when there is only one generic type with that name. 
Additionally to that, I would prohibit using `_` in the same places as `var`.

</details>

## Design meetings

Link to design notes that affect this proposal, and describe in one sentence for each what changes they led to.