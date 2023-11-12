# Guidelines

C# is a strongly and statically typed, object-oriented programming language developed by Microsoft. 
Since types have to be known in compilation time, a type inference was introduced to reduce typing of obvious type annotations. 
However, type inference in C# is not as strong as we can see in other strongly statically typed languages like RUST or Haskell.

The goal of this thesis is to investigate the boundaries of C# type inference and compare it with existing implementations in different languages. 
Then should propose improvements regarding the type inference based on the previous analysis and should create a prototype.
During the work, the author should closely work with the Roslyn team to make the proposal more likely to be discussed by the committee for C# standard and the Roslyn team and maybe later accepted.

## References

1. C# documentation, https://learn.microsoft.com/en-us/dotnet/csharp/
2. Workspace for C# language specification, https://github.com/dotnet/csharpstandard
3. .NET Compiler Platform, Roslyn,  https://github.com/dotnet/roslyn
4. RUST compiler guide, https://rustc-dev-guide.rust-lang.org/
