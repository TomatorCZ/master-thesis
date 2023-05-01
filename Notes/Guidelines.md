# Guidelines

C# is a strongly and statically typed, object-oriented programming language developed by Microsoft. 
Since types have to be known in compilation time, a type inference was introduced to reduce typing of obvious type annotations. 
However, type inference in C# is not as strong as we can see in different strongly statically typed languages like RUST. 
Since Roslyn is an open-source compiler of C#, an open community has joined the development of the language and the compiler.

This thesis aims to investigate the boundaries of C# type inference and compare it with existing implementations in different languages. 
Then proposes improvements regarding the type inference based on the previous analysis and creates a prototype. 
During the work, we will closely work with Roslyn team to make the proposal more likely to be accepted by the commitee for C# standard and the Roslyn team.

## References

1. C# documentation, https://learn.microsoft.com/en-us/dotnet/csharp/
2. Roslyn, https://github.com/dotnet/roslyn
3. Hindley-Milner type inference, https://en.wikipedia.org/wiki/Hindley%E2%80%93Milner_type_system
