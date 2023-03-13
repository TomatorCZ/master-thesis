# Improve type inference when inhereting multible single generic interface

## My notes

> Source: 
> [csharplang/discussions#1592](https://github.com/dotnet/csharplang/discussions/1592)

**Idea**
Looks like the algorithm is weak.

**Examples**

```c#
interface I<T> { }
class X<T> { }
class Y { }
class Z { }
class C : I<X<Y>>, I<Z> { }
public class P
{
	static void M<T>(I<X<T>> i) { } //Error
	public static void Main()
	{
		M(new C());
	}
}
```

## Proposal