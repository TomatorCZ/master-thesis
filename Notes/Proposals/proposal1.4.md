# Default type parameters

## My notes

> Source:
> [csharplang/discussion#278](https://github.com/dotnet/csharplang/discussions/278),
> [roslyn/issues#6248](https://github.com/dotnet/roslyn/issues/6248),
> [csharplang/discussions#4035](https://github.com/dotnet/csharplang/discussions/4035)

**Idea**

Allowing to define predefined type arguments to not borther with specifying it in usage. Can be combined well with skipping type args list.

**Examples**

Basic usage can look like this. ([csharplang/discussion#278](https://github.com/dotnet/csharplang/discussions/278))

```c#
class X<T = object> {}
...
var x = new X();
```

Further improvments can evolve `this` as default. ([roslyn/issues#6248](https://github.com/dotnet/roslyn/issues/6248))

```c#
interface IEquitable<T = this> {}

class X : IEquitable {} // IEquitable<X>
```

Also, the default type parameters could be resolved be default parameters. ([csharplang/discussions#4035](https://github.com/dotnet/csharplang/discussions/4035))
```c#
public static void Bar<A>(A a=42) {}
...
Bar();
```

It could have syntax in style of attributes. ([csharplang/discussion#278](https://github.com/dotnet/csharplang/discussions/278))

```c#
class X<[DefaultGenericArgument(typeof(C))] T> where T : C
```

## Proposal