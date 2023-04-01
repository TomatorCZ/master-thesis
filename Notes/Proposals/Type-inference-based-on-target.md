# Type inference based on target

## Summary

The compiler doesn't use target type information in type inference of 
1. Methods
2. Constructors
3. Collection initilizers
4. switch expressions and deconstruction

We feel that it is not ideal and propose the type inference improvement.

## Motivation

In the current situation, C# doesn't  use target for inference in scenarios like a collection creation or calling generic method.
We can improve the inference of

1. Generic methods
2. Constructors
3. Collection initializers

by using targets at the following places in the code

1. RHS of assignment operator
2. Call argument
3. Field or Property
4. Return statement 

and combination of switch expression with deconstruction.

## Detailed design

This improvement aims to improve current inference by using the type information of a target as another source of type constraint. It is combined with type information obtained from arguments and other potential sources.

### Generic methods

If we have a generic method having type argument/s used in the return type, we can use the target as a type constraint in the inference algorithm. 

Suppose we have a static method that can read named property from `object`. 
We will demonstrate the potential use of the target's type information during the inference. 
In this example, we chose a simple case, although we can imagine that the return type could be something more complex like `Result<TOk, TError>`, where `Result` is a generic class.

```csharp
public static class ObjectEx {
     public static T Field<T>(this object obj, string propName) {...}
}
```

**Assignment operator**

There is a primitive case showing an unnecessary specified type argument. 
One can see the usage in this example as useless which is true.
However, imagine `Select` method from LINQ where is a common scenario to use this kind of extractor to map it on a different entity by object initializer.

```csharp
object uknownObject = ...
// Ok
int age = uknownObject.Field<int>("Age");
// Error
int age = uknownObject.Field("Age"); 
```

**Call argument**

The target can be an argument of method.

```csharp
public static void Consumer(string message) {...}
...
object uknownObject...
// Ok
Consumer(uknownObject.Field<string>("message"));
// Error
Consumer(uknownObject.Field("message"));
```

**Field/Property**

Another source of type information is a field or property of class/struct. Imagine some static factory that creates objects by the generic `Create<TResult>` method.

```csharp
class Foo {
     // Ok
     int Bar {get; set;} = Factory.Create<int>("data"); 
     // Error
     int Bar {get; set;} = Factory.Create("data"); 
}
```

**Return statement**

In the end, there are situations where we repeat ourselves in return statements.

```csharp
static int Foo(...) {
     ...
     // Ok
     return obj.Field("Foo");
     // Error
     return obj.Field("Foo");
}
```

### Constructors

We see the feature as beneficial for constructors as well in scenarios where the target is an abstract generic type and we just want to specify the implementation. 
The concept is rather the same as in previous code segments, so we are showing just one of them.
We chose again a simple scenario with only one type argument, although it can be extended and be as powerful as method type inference.

```csharp
public static void Consumer(IReadOnlyList<Bar> sequence) {...}
...
//Ok
Consumer(new List<Bar>());
//Error
Consumer(new List());
```

### Collection initializers

We noticed the compiler doesn't use a target to determine the type of collection and items in the initializer list.
We have to specify the type of items in the collection, which is unnecessary.

```csharp
//OK
IEnumerable<KeyValuePair<string, string>> items1 = new[]
{
     new KeyValuePair<string, string>("Foo", "foo"),
     new KeyValuePair<string, string>("Bar", "bar"),
};

IEnumerable<KeyValuePair<string, string>> items2 = new List<KeyValuePair<string, string>>()
{
     new KeyValuePair<string, string>("Foo", "foo"),
     new KeyValuePair<string, string>("Bar", "bar"),
};

//Error

IEnumerable<KeyValuePair<string, string>> items3 = new[]
{
     new("Foo", "foo"),
     new("Bar", "bar"),
};

IEnumerable<KeyValuePair<string, string>> items4 = new List()
{
     new("Foo", "foo"),
     new("Bar", "bar"),
};
```

Also, we would like to use just constructors without initializers.

```csharp
//Ok
int[] item1 = new int[1];

//Error
int[] item = new[1];
```

> Already implemented ?? https://github.com/dotnet/csharplang/issues/2701

### Switch expression and deconstruction

We noticed unsupported inference of switch expression with deconstruction.
Once we don't see any negative consequences of this improvement, we would like to support it as well.

```csharp
MemberInfo info = null;
Action<object> getValue = null;
Type type = null;

//Error
(type, getValue) = info switch
{
    PropertyInfo pi => (pi.PropertyType, () => pi.GetValue(d)),
    FieldInfo fi => (fi.FieldType, () => fi.GetValue(d))
};
```

## Required changes

### Syntax

1. Collection initilizers ?
2. array constructor ?

### Inference algorithm

>TBF





