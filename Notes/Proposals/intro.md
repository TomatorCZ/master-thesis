# Intro

This intro lists features regarding partial type inference, which will be targets of the master thesis. They are devided into two groups. First group consists of features, which are likely to be accepted by LDM. Second group consists of features, which are interesting in terms of implementation and design, but are experimental.

## Probably will be accepted by LDM

- Explicit partial type inference
- Type inference based on target
- (0.3) Constructor type inference
  - ParameterLess constructor
  - target-types new[] initializers
  - object and collection initilizers
- (0.4) Type inference using implicit operators
- (0.5) Type Inference of ref/out params in lambdas
- (0.6) Improve type inference when inhereting multible single generic interface
- (0.8) Improve infrence of type deconstruction

## I like to pick it up

- (1.0) Constrained type inference
- (1.1) Inference based on later methods call
- (1.2) Improving delegate overload resolution
- (1.3) Type inference by method group
- (1.4) Default type parameters
- (1.5) Named typed arguments
- (1.6) Aliases defining partial type arguments
- (1.7) Existential types
- Inference of type signatures for single expression or void...
  - https://learn.microsoft.com/en-gb/archive/blogs/ericlippert/why-no-var-on-fields
  - Why kotlin is able to do that ?
  - 
