# Type inference using implicit operators

## My notes

> Source:
> [csharplang/discussions#2067](https://github.com/dotnet/csharplang/discussions/2067)

**Idea**

Inference will take into consideration implicit operators.

**Examples**

```c#
class Wrapper<T> {
    public T Value { get; }
    public Wrapper(T value){ this.Value = value; }
    public static implicit operator Wrapper<T>(T value) => new Wrapper<T>(value);
}
void Consume<T>(Wrapper<T> wrapper) { /* some code */ }
...
int i = 0;
Consume(i);
```

## Proposal