# Meeting

## Agenda

1. What I have done

Why I firstly do prototype?
Mention reasonining behind diferrent strenght of type inference in invocations and constructors

Method type inference
    - Using `_` in every identifier which is invocation expression
    - Using `_` as an inferred type argument
    - allow nested `_` in the argument list
    - allowing to use `_?`
    - Cutting of `_` type hints in dynamic checks
    - Nullable analysis (Why it was easy...)
Constructor type inference
    - Using `_` as an inferred type argument
    - allow nested `_` in the argument list
    - allowing to use `_?`
    - warning when used in dynamic call
    - using target in the inference
    - using initializer lists in the inference
    - using where clauses in the inference
    - using diamond operator
    - Nullable analysis when there is no diamond operator

2. What I'm planning to do

Cnstructor type inference
    - nullable analysis for diamond operator

3. Is it sufficient for proposal ? (I guess we don't want to make very big changes in it, just to set the starter idea and suggest possible directions) -> If yes, I would finish with the implementation and start to write the proposal

- Should I also improve(implement) type inference for array creations ?
    - It would be as strong as for Method type inference otherwise it would introduce breaking changes (Can't use info about target with initializer list)
- Should I also implement type inference for delegate creations ?
- Can I omit (the implementation of) partial defined type variables `G<_>` and casts ? (I would make note in the proposal as a future improvement)

//G<_> a = ...
G a = new()
