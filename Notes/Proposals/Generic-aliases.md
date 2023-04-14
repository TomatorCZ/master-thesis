# Generic aliases

## Detailed design

A basic principle of generic aliases would be to be able to specify type parameters, which can be used as type arguments of aliased type.
Since the generics includes also `where` clauses and proposed default parameters, we have to decide if we should allow them also for aliases.

For a broader sight, `where` clauses are used to restrict used type parameters for two reasons.
The first one restricts it because the type uses the restricted type parameter in a specific way. 
The second one restricts it because it implements a base `class` or `interface` requiring the restriction.
Based on this knowledge, it looks like we should allow it and even require it in the cases when aliased type requires type parameter restriction.
However, we don't consider an alias to be a new type.
It just refers to it.
Introducing the `where` clauses allowed them to add new restrictions to the existing type.
We don't feel that aliases should be able to do that.
So we disallow the `where` clauses and just "inherit" the restriction from aliased type.

The second one consists of the default parameter.
It can be tricky.
As we described lowering default parameters, involves a parameter type attribute representing the default value.
If we enable the default parameter in aliases, it wouldn't have the same meaning as a default parameter placed in aliased type.
We can't modify attributes contained in the third-party library, so the alias would again add "new information" (attribute which can be obtained by reflection) into that type which is not desired.
So we don't allow to use it in aliases.

Although using aliases doesn't completely replace the default parameters, it helps to do it in a restricted way in the case of third-party libraries which will don't use default type parameters.
Look at the example.

```csharp
global using MyBase<T> = BaseClass<T, T, int, string>;

class BaseClass<T1, T2, T3, T4> 
where ...
{...}
```