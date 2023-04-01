# F# type inference

>Source: [fsharp](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/type-inference)

Generics definition in F# is implicit.

```F#
let f a b = a + b + ?
// ? = int -> a : int, b : int
// ? = "" -> a : string, b : string
```

Types can be specified in body

```F#
// Type annotations on a parameter.
let addu1 (x : uint32) y =
    x + y

// Type annotations on an expression.
let addu2 x y =
    (x : uint32) + y
```

Automatic generalization
```F#
let makeTuple a b = (a, b)
//type : 'a -> 'b -> 'a * 'b
```

Type abbreviation

```F#
type SizeType = uint32

type Transform<'a> = 'a -> 'a
```

Inference based on used types

```F#
let makeList a b = [a; b]
// a' -> a' -> a' list
```

```F#
let function1 (x: 'a) (y: 'a) =
    printfn "%A %A" x y
```

Explicit generic

```F#
let function2<'T> (x: 'T) (y: 'T) =
    printfn "%A, %A" x y

function2<int> ...
```

Wildcards

```F#
let printSequence (sequence1: Collections.seq<_>) =
   Seq.iter (fun elem -> printf "%s " (elem.ToString())) sequence1
```

# Kotlin type inference

> Kotlin infrence https://kotlinlang.org/spec/type-inference.html

## Inference based on call arguments

> Source: [kotlinlang.org](https://kotlinlang.org/docs/generics.html)

```kotlin
class Box<T>(t: T) {
    var value = t
}

val box1: Box<Int> = Box<Int>(1)
val box2 = Box(1) // T = Int
```

## Inference based on call target

```kotlin
fun<T> T.foo() {
    // sth
}

fun bar() {
    1.foo() // T = Int
}

```

## Star projection

Useful for controlling covariance and contravariance.

> Source: [kotlinlang.org](https://kotlinlang.org/docs/generics.html)

```kotlin
interface Function<in T, out U>
//
Function<*, String> ~ Function<in Nothing, String>
Function<Int, *> ~ Function<Int, *>
Function<*, *> ~ Function<in Nothing, out Any?>
```


## Partial inference - Underscore operator

> Source: [kotlinlang.org](https://kotlinlang.org/docs/generics.html)

```kotlin
abstract class SomeClass<T> {
    abstract fun execute() : T
}

class SomeImplementation : SomeClass<String>() {
    override fun execute(): String = "Test"
}

class OtherImplementation : SomeClass<Int>() {
    override fun execute(): Int = 42
}

object Runner {
    inline fun <reified S: SomeClass<T>, T> run() : T {
        return S::class.java.getDeclaredConstructor().newInstance().execute()
    }
}

fun main() {
    // T is inferred as String because SomeImplementation derives from SomeClass<String>
    val s = Runner.run<SomeImplementation, _>()
    assert(s == "Test")

    // T is inferred as Int because OtherImplementation derives from SomeClass<Int>
    val n = Runner.run<OtherImplementation, _>()
    assert(n == 42)
}
``` 

## Nested underscore operator

> Source: [kotlinlang.org](https://kotlinlang.org/docs/generics.html)

```kotlin
abstract class SomeClass<T> {
    abstract fun execute() : List<T>
}

class SomeImplementation : SomeClass<String>() {
    override fun execute(): List<String> = listOf("Test")
}

object Runner {
    inline fun <reified S: SomeClass<T>, T> run() : T {
        return S::class.java.getDeclaredConstructor().newInstance().execute()
    }
}

fun main() {
    // T is inferred as String because SomeImplementation derives from SomeClass<String>
    val s = Runner.run<SomeImplementation, List<_>>() // List<[Error type: Unresolved type for _]>
}
```

```kotlin
class Bar<T1, T2>(temp: T2) {}

fun<T1> foo() {}

fun main() {
    val a : Bar<Int, _>; // Unresolved reference: _
    a = Bar("")
}

```

# Rust type inference

https://doc.rust-lang.org/rust-by-example/types/inference.html