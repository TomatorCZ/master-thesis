// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal static class PooledDictionaryIgnoringNullableModifiersForReferenceTypes
    {
        private static readonly ObjectPool<PooledDictionary<NamedTypeSymbol, NamedTypeSymbol>> s_poolInstance
            = PooledDictionary<NamedTypeSymbol, NamedTypeSymbol>.CreatePool(Symbols.SymbolEqualityComparer.IgnoringNullable);

        internal static PooledDictionary<NamedTypeSymbol, NamedTypeSymbol> GetInstance()
        {
            var instance = s_poolInstance.Allocate();
            Debug.Assert(instance.Count == 0);
            return instance;
        }
    }

    internal readonly struct TypeInferenceResult
    {
        public readonly ImmutableArray<TypeWithAnnotations> InferredTypeArguments;
        public readonly ImmutableArray<TypeWithAnnotations> InferredTypeVariables;
        public readonly bool HasTypeArgumentInferredFromFunctionType;
        public readonly bool Success;

        public TypeInferenceResult(
            bool success,
            ImmutableArray<TypeWithAnnotations> inferredTypeArguments,
            ImmutableArray<TypeWithAnnotations> inferredTypeVariables,
            bool hasTypeArgumentInferredFromFunctionType)
        {
            this.Success = success;
            this.InferredTypeArguments = inferredTypeArguments;
            this.HasTypeArgumentInferredFromFunctionType = hasTypeArgumentInferredFromFunctionType;
            this.InferredTypeVariables = inferredTypeVariables;
        }
    }

    internal sealed class TypeInferrer
    {
        internal abstract class Extensions
        {
            internal static readonly Extensions Default = new DefaultExtensions();

            internal abstract TypeWithAnnotations GetTypeWithAnnotations(BoundExpression expr);

            internal abstract TypeWithAnnotations GetMethodGroupResultType(BoundMethodGroup group, MethodSymbol method);

            private sealed class DefaultExtensions : Extensions
            {
                internal override TypeWithAnnotations GetTypeWithAnnotations(BoundExpression expr)
                {
                    return TypeWithAnnotations.Create(expr.GetTypeOrFunctionType());
                }

                internal override TypeWithAnnotations GetMethodGroupResultType(BoundMethodGroup group, MethodSymbol method)
                {
                    return method.ReturnTypeWithAnnotations;
                }
            }
        }

        private sealed class EqualsIgnoringDynamicTupleNamesAndNullabilityComparer : EqualityComparer<TypeWithAnnotations>
        {
            internal static readonly EqualsIgnoringDynamicTupleNamesAndNullabilityComparer Instance = new EqualsIgnoringDynamicTupleNamesAndNullabilityComparer();

            public override int GetHashCode(TypeWithAnnotations obj)
            {
                return obj.Type.GetHashCode();
            }

            public override bool Equals(TypeWithAnnotations x, TypeWithAnnotations y)
            {
                if (x.Type.IsDynamic() ^ y.Type.IsDynamic()) { return false; }

                return x.Equals(y, TypeCompareKind.IgnoreDynamicAndTupleNames | TypeCompareKind.IgnoreNullableModifiersForReferenceTypes);
            }
        }

        private enum InferenceResult
        {
            InferenceFailed,
            MadeProgress,
            NoProgress,
            Success
        }

        private enum ExactOrShapeOrBoundsKind
        {
            Exact,
            Shape,
            LowerBound,
            UpperBound,
        }

        private enum Dependency
        {
            Unknown = 0x00,
            NotDependent = 0x01,
            DependsMask = 0x10,
            Direct = 0x11,
            Indirect = 0x12
        }

        private enum DependencyKind
        {
            Function,
            Bound,
            Shape
        }

        #region State
        private readonly CSharpCompilation _compilation;
        private readonly ConversionsBase _conversions;
        private readonly ImmutableArray<TypeSymbol> _typeVariables;
        private readonly NamedTypeSymbol _constructedContainingType;
        private readonly ImmutableArray<TypeWithAnnotations> _targets;
        private readonly ImmutableArray<BoundExpression> _sources;
        private readonly ImmutableArray<ExactOrShapeOrBoundsKind> _boundKinds;
        private readonly Extensions _extensions;

        private readonly (TypeWithAnnotations Type, bool FromFunctionType)[] _fixedResults;

        private readonly TypeWithAnnotations[] _typeVariableShapes;
        private readonly HashSet<TypeWithAnnotations>[] _exactBounds;
        private readonly HashSet<TypeWithAnnotations>[] _upperBounds;
        private readonly HashSet<TypeWithAnnotations>[] _lowerBounds;
        private readonly NullableAnnotation[] _nullableAnnotationLowerBounds;

        private Dependency[,] _functionDependencies;
        private bool _functionDependenciesDirty;
        private Dependency[,] _boundDependencies;
        private bool _boundDependenciesDirty;
        private Dependency[,] _shapeDependencies;
        private bool _shapeDependenciesDirty;

        private readonly TypeVariableMap _map;
        #endregion

        private TypeInferrer(
            CSharpCompilation compilation,
            ConversionsBase conversions,
            NamedTypeSymbol constructedContainingType,
            ImmutableArray<TypeSymbol> typeVariables,
            ImmutableArray<TypeWithAnnotations> targets,
            ImmutableArray<BoundExpression> sources,
            ImmutableArray<ExactOrShapeOrBoundsKind> boundKinds,
            Extensions extensions)
        {
            _compilation = compilation;
            _conversions = conversions;
            _constructedContainingType = constructedContainingType;
            _typeVariables = typeVariables;
            _targets = targets;
            _sources = sources;
            _boundKinds = boundKinds;
            _extensions = extensions ?? Extensions.Default;
            _fixedResults = new (TypeWithAnnotations, bool)[_typeVariables.Length];
            _upperBounds = new HashSet<TypeWithAnnotations>[_typeVariables.Length];
            _lowerBounds = new HashSet<TypeWithAnnotations>[_typeVariables.Length];
            _exactBounds = new HashSet<TypeWithAnnotations>[_typeVariables.Length];
            _nullableAnnotationLowerBounds = new NullableAnnotation[_typeVariables.Length];
            Debug.Assert(_nullableAnnotationLowerBounds.All(annotation => annotation.IsNotAnnotated()));
            _functionDependencies = null;
            _functionDependenciesDirty = false;
            _boundDependencies = null;
            _boundDependenciesDirty = false;
            _shapeDependencies = null;
            _shapeDependenciesDirty = false;
            _typeVariableShapes = new TypeWithAnnotations[_typeVariables.Length];
            _map = constructedContainingType.TypeSubstitution?.AsTypeVariableMap() ?? new TypeVariableMap();
        }

        #region Public API
        private static TypeInferenceResult Infer(
            Binder binder,
            ConversionsBase conversions,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            NamedTypeSymbol containingTypeOfConstructor,
            Extensions extensions,
            ImmutableArray<TypeWithAnnotations> targets,
            ImmutableArray<BoundExpression> sources,
            ImmutableArray<ExactOrShapeOrBoundsKind> boundKinds,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo,
            ImmutableArray<SourceInferredTypeSymbol> inferredTypeParameters = default)
        {
            if (targets.Length == 0)
            {
                return new TypeInferenceResult(success: false, inferredTypeArguments: default, inferredTypeVariables: default, hasTypeArgumentInferredFromFunctionType: false);
            }

            var typeVariables = ArrayBuilder<TypeSymbol>.GetInstance();
            typeVariables.AddRange(typeParameters);
            if (!inferredTypeParameters.IsDefault)
                typeVariables.AddRange(inferredTypeParameters);

            var inferrer = new TypeInferrer(
                binder.Compilation,
                conversions,
                containingTypeOfConstructor,
                typeVariables.ToImmutableAndFree(),
                targets,
                sources,
                boundKinds,
                extensions);

            return inferrer.InferTypeVars(binder, ref useSiteInfo);
        }

        private static void AddArguments(
            ArrayBuilder<TypeWithAnnotations> targets,
            ArrayBuilder<BoundExpression> sources,
            ArrayBuilder<ExactOrShapeOrBoundsKind> bounds,
            ImmutableArray<TypeWithAnnotations> parameterTypes,
            ImmutableArray<BoundExpression> arguments,
            ImmutableArray<RefKind> formalParameterRefKinds,
            HashSet<TypeParameterSymbol> typeParameters)
        {
            if (parameterTypes.IsDefault || arguments.IsDefault)
                return;

            int min = Math.Min(parameterTypes.Length, arguments.Length);
            for (int i = 0; i < min; i++)
            {
                targets.Add(parameterTypes[i]);
                //Don't forget to add diagnostics.
                if (arguments[i] is BoundUnconvertedInferredClassCreationExpression expr && parameterTypes[i].Type.ContainsTypeParameters(typeParameters))
                    sources.Add(expr.BoundWithoutTargetTypeExpression);
                else
                    sources.Add(arguments[i]);

                bounds.Add(((!formalParameterRefKinds.IsDefault && formalParameterRefKinds[i].IsManagedReference()) || arguments[i].Type.IsPointerType())
                    ? ExactOrShapeOrBoundsKind.Exact
                    : ExactOrShapeOrBoundsKind.LowerBound);
            }
        }

        private static void AddTypeArguments(
           ArrayBuilder<TypeWithAnnotations> targets,
           ArrayBuilder<BoundExpression> sources,
           ArrayBuilder<ExactOrShapeOrBoundsKind> bounds,
           ImmutableArray<TypeParameterSymbol> typeParameters,
           ImmutableArray<TypeWithAnnotations> typeArguments)
        {
            if (typeArguments.IsDefault || typeArguments.Length == 0)
                return;

            targets.AddRange(typeParameters.Select(x => TypeWithAnnotations.Create(x)));
            sources.AddRange(typeArguments.Select(x => new BoundTypeExpression(x.Type.GetNonNullSyntaxNode(), null, x)));
            for (int i = 0; i < typeArguments.Length; i++)
                bounds.AddRange(ExactOrShapeOrBoundsKind.Shape);
        }

        private static void AddTarget(
            ArrayBuilder<TypeWithAnnotations> targets,
            ArrayBuilder<BoundExpression> sources,
            ArrayBuilder<ExactOrShapeOrBoundsKind> bounds,
            TypeWithAnnotations source,
            TypeWithAnnotations target,
            bool isRef)
        {
            targets.Add(target);
            sources.Add(new BoundTypeExpression(source.Type.GetNonNullSyntaxNode(), null, source));
            bounds.Add((isRef || source.Type.IsPointerType())
                    ? ExactOrShapeOrBoundsKind.Exact
                    : ExactOrShapeOrBoundsKind.UpperBound);
        }

        private static void AddConstraints(
            ArrayBuilder<TypeWithAnnotations> targets,
            ArrayBuilder<BoundExpression> sources,
            ArrayBuilder<ExactOrShapeOrBoundsKind> bounds,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            ImmutableArray<ImmutableArray<TypeWithAnnotations>> typeParametersConstraints
        )
        {
            for (int i = 0; i < typeParameters.Length; i++)
            {
                var constraints = typeParametersConstraints[i];
                for (int j = 0; j < constraints.Length; j++)
                {
                    targets.Add(TypeWithAnnotations.Create(typeParameters[i]));
                    sources.Add(new BoundTypeExpression(constraints[j].Type.GetNonNullSyntaxNode(), null, constraints[j]));
                    bounds.Add(ExactOrShapeOrBoundsKind.UpperBound);
                }
            }
        }

        public static TypeInferenceResult InferConstructor(
            Binder binder,
            ConversionsBase conversions,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            NamedTypeSymbol containingTypeOfConstructor,
            ImmutableArray<TypeWithAnnotations> formalParameterTypes,
            ImmutableArray<RefKind> formalParameterRefKinds,
            ImmutableArray<BoundExpression> arguments,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo,
            ImmutableArray<SourceInferredTypeSymbol> inferredTypeParameters = default,
            ImmutableArray<TypeWithAnnotations> typeArguments = default,
            (TypeWithAnnotations source, TypeWithAnnotations target, bool isRef) targetContraint = default,
            ImmutableArray<ImmutableArray<TypeWithAnnotations>> typeParametersConstraints = default,
            Extensions extensions = null
            )
        {
            var targets = ArrayBuilder<TypeWithAnnotations>.GetInstance();
            var sources = ArrayBuilder<BoundExpression>.GetInstance();
            var boundKinds = ArrayBuilder<ExactOrShapeOrBoundsKind>.GetInstance();

            AddArguments(targets, sources, boundKinds, formalParameterTypes, arguments, formalParameterRefKinds, new HashSet<TypeParameterSymbol>(typeParameters));
            AddTypeArguments(targets, sources, boundKinds, typeParameters, typeArguments);
            if (!targetContraint.source.IsDefault && !targetContraint.target.IsDefault)
                AddTarget(targets, sources, boundKinds, targetContraint.source, targetContraint.target, targetContraint.isRef);

            if (!typeParametersConstraints.IsDefault)
                AddConstraints(targets, sources, boundKinds, typeParameters, typeParametersConstraints);

            return Infer(
                binder,
                conversions,
                typeParameters,
                containingTypeOfConstructor,
                extensions,
                targets.ToImmutableAndFree(),
                sources.ToImmutableAndFree(),
                boundKinds.ToImmutableAndFree(),
                ref useSiteInfo,
                inferredTypeParameters
            );
        }

        public static TypeInferenceResult InferMethod(
            Binder binder,
            ConversionsBase conversions,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            NamedTypeSymbol containingTypeOfConstructor,
            ImmutableArray<TypeWithAnnotations> formalParameterTypes,
            ImmutableArray<RefKind> formalParameterRefKinds,
            ImmutableArray<BoundExpression> arguments,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo,
            ImmutableArray<SourceInferredTypeSymbol> inferredTypeParameters = default,
            ImmutableArray<TypeWithAnnotations> typeArguments = default,
            Extensions extensions = null
        )
        {
            var targets = ArrayBuilder<TypeWithAnnotations>.GetInstance();
            var sources = ArrayBuilder<BoundExpression>.GetInstance();
            var boundKinds = ArrayBuilder<ExactOrShapeOrBoundsKind>.GetInstance();

            AddArguments(targets, sources, boundKinds, formalParameterTypes, arguments, formalParameterRefKinds, new HashSet<TypeParameterSymbol>(typeParameters));
            AddTypeArguments(targets, sources, boundKinds, typeParameters, typeArguments);

            return Infer(
                binder,
                conversions,
                typeParameters,
                containingTypeOfConstructor,
                extensions,
                targets.ToImmutableAndFree(),
                sources.ToImmutableAndFree(),
                boundKinds.ToImmutableAndFree(),
                ref useSiteInfo,
                inferredTypeParameters
            );
        }

        public static ImmutableArray<TypeWithAnnotations> InferTypeArgumentsFromFirstArgument(
            CSharpCompilation compilation,
            ConversionsBase conversions,
            MethodSymbol method,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            ImmutableArray<BoundExpression> arguments,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (!CanInferTypeArgumentsFromFirstArgument(compilation, conversions, method, typeParameters, arguments, ref useSiteInfo, out var inferrer))
            {
                return default;
            }

            return inferrer.GetInferredTypeVariables(out _).Where(x => x.Item1.Kind == SymbolKind.TypeParameter).Select(x => x.Item2).AsImmutable();
        }

        public static bool CanInferTypeArgumentsFromFirstArgument(
            CSharpCompilation compilation,
            ConversionsBase conversions,
            MethodSymbol method,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            ImmutableArray<BoundExpression> arguments,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo,
            out TypeInferrer inferrer,
            ImmutableArray<TypeWithAnnotations> typeArgumentHints = default,
            ImmutableArray<SourceInferredTypeSymbol> inferredTypeParameters = default)
        {
            Debug.Assert((object)method != null);
            Debug.Assert(method.Arity > 0);
            Debug.Assert(!arguments.IsDefault);

            // We need at least one formal parameter type and at least one argument.
            if ((method.ParameterCount < 1) || (arguments.Length < 1))
            {
                inferrer = default;
                return false;
            }

            Debug.Assert(!method.GetParameterType(0).IsDynamic());

            var targets = ArrayBuilder<TypeWithAnnotations>.GetInstance();
            var sources = ArrayBuilder<BoundExpression>.GetInstance();
            var boundKinds = ArrayBuilder<ExactOrShapeOrBoundsKind>.GetInstance();

            //Add first argument
            AddArguments(targets, sources, boundKinds,
                method.GetParameterTypes().Take(1).AsImmutable(),
                arguments.Take(1).AsImmutable(),
                (!method.ParameterRefKinds.IsDefault && method.ParameterRefKinds.Length > 0) ? method.ParameterRefKinds.Take(1).AsImmutable() : default,
                new HashSet<TypeParameterSymbol>(method.TypeParameters)
            );

            //Add type hints
            AddTypeArguments(targets, sources, boundKinds, typeParameters, typeArgumentHints);

            var typeVariables = ArrayBuilder<TypeSymbol>.GetInstance();
            typeVariables.AddRange(typeParameters);
            if (!inferredTypeParameters.IsDefault)
                typeVariables.AddRange(inferredTypeParameters);

            inferrer = new TypeInferrer(
                compilation,
                conversions,
                method.ContainingType,
                typeVariables.ToImmutableAndFree(),
                targets.ToImmutableAndFree(),
                sources.ToImmutableAndFree(),
                boundKinds.ToImmutableAndFree(),
                extensions: null);

            if (!inferrer.InferTypeArgumentsInFirstGivenTarget(ref useSiteInfo))
            {
                return false;
            }


            return true;
        }
        #endregion

        #region Phases
        private bool InferTypeArgumentsInFirstGivenTarget(ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            foreach (var source in _sources)
            {
                if (!IsReallyAType(source.Type))
                    return false;
            }

            //Do inference
            InferTypeVarsFirstPhase(null, ref useSiteInfo);
            InferTypeVarsSecondPhase(null, ref useSiteInfo);

            //Check if the first target doesn't contain unfixed type parameter.
            var substituted = _map.SubstituteType(_targets[0]).Type;
            if (substituted.IsInferred() || substituted.ContainsTypeParameters(new HashSet<TypeParameterSymbol>(_typeVariables.Where(x => x.IsTypeParameter()).Cast<TypeParameterSymbol>())))
                return false;

            return true;
        }

        private TypeInferenceResult InferTypeVars(Binder binder, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            InferTypeVarsFirstPhase(binder, ref useSiteInfo);
            bool success = InferTypeVarsSecondPhase(binder, ref useSiteInfo);
            var inferredTypeVariables = GetResults(out bool inferredFromFunctionType);
            return new TypeInferenceResult(
                success,
                inferredTypeVariables.Where(x => x.Item1.Kind == SymbolKind.TypeParameter).Select(x => x.Item2).ToImmutableArray(),
                inferredTypeVariables.Where(x => x.Item1.Kind == SymbolKindInternal.InferredType).Select(x => x.Item2).ToImmutableArray(),
                inferredFromFunctionType);
        }

        #region The first phase
        private void InferTypeVarsFirstPhase(Binder binder, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            InitializeDependencies(DependencyKind.Shape);
            InitializeDependencies(DependencyKind.Bound);

            for (int arg = 0, length = _sources.Length; arg < length; arg++)
            {
                BoundExpression source = _sources[arg];
                TypeWithAnnotations target = _targets[arg];
                ExactOrShapeOrBoundsKind kind = _boundKinds[arg];

                if (kind == ExactOrShapeOrBoundsKind.Shape)
                {
                    AddBoundAndDoFurtherInference(((BoundTypeExpression)source).TypeWithAnnotations, target, kind, ref useSiteInfo);
                }
                else
                {
                    MakeExplicitParameterTypeInferences(binder, source, target, kind, ref useSiteInfo);
                }
            }
        }

        private void MakeExplicitParameterTypeInferences(Binder binder, BoundExpression source, TypeWithAnnotations target, ExactOrShapeOrBoundsKind kind, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (source.Kind == BoundKind.UnboundLambda && target.Type.GetDelegateType() is { })
            {
                ExplicitParameterTypeInference(source, target, ref useSiteInfo);
                ExplicitReturnTypeInference(source, target, ref useSiteInfo);
            }
            else if (source.Kind == BoundKind.UnconvertedCollectionExpression)
            {
                MakeCollectionExpressionTypeInferences(binder, (BoundUnconvertedCollectionExpression)source, target, kind, ref useSiteInfo);
            }
            else if (source.Kind != BoundKind.TupleLiteral ||
                !MakeExplicitParameterTypeInferences(binder, (BoundTupleLiteral)source, target, kind, ref useSiteInfo))
            {
                var argumentType = _extensions.GetTypeWithAnnotations(source);
                if (IsReallyAType(argumentType.Type))
                {
                    ExactOrBoundsInference(kind, argumentType, target, ref useSiteInfo);
                }
                else if (IsUnfixedTypeVariable(target) && !target.NullableAnnotation.IsAnnotated() && kind is ExactOrShapeOrBoundsKind.LowerBound)
                {
                    var idx = GetTypeVariableIndex(target.Type);
                    _nullableAnnotationLowerBounds[idx] = _nullableAnnotationLowerBounds[idx].Join(argumentType.NullableAnnotation);
                }
            }
        }

        private void MakeCollectionExpressionTypeInferences(
            Binder binder,
            BoundUnconvertedCollectionExpression argument,
            TypeWithAnnotations target,
            ExactOrShapeOrBoundsKind kind,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            TypeSymbol targetType = target.Type;
            Debug.Assert(targetType is { });
            if (targetType is null)
            {
                return;
            }

            if (argument.Elements.Length == 0)
            {
                return;
            }

            if (!binder.TryGetCollectionIterationType((ExpressionSyntax)argument.Syntax, targetType.StrippedType(), out TypeWithAnnotations targetElementType))
            {
                return;
            }

            foreach (var element in argument.Elements)
            {
                if (element is BoundCollectionExpressionSpreadElement spread)
                {
                    MakeSpreadElementTypeInferences(spread, targetElementType, ref useSiteInfo);
                }
                else
                {
                    MakeExplicitParameterTypeInferences(binder, (BoundExpression)element, targetElementType, kind, ref useSiteInfo);
                }
            }
        }

        private void MakeSpreadElementTypeInferences(
            BoundCollectionExpressionSpreadElement argument,
            TypeWithAnnotations target,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(target.Type is { });
            if (target.Type is null)
            {
                return;
            }

            var enumeratorInfo = argument.EnumeratorInfoOpt;
            if (enumeratorInfo is null)
            {
                return;
            }

            LowerBoundInference(enumeratorInfo.ElementTypeWithAnnotations, target, ref useSiteInfo);
        }

        private bool MakeExplicitParameterTypeInferences(Binder binder, BoundTupleLiteral source, TypeWithAnnotations target, ExactOrShapeOrBoundsKind kind, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (target.Type.Kind != SymbolKind.NamedType)
            {
                return false;
            }

            var destination = (NamedTypeSymbol)target.Type;
            var sourceArguments = source.Arguments;

            if (!destination.IsTupleTypeOfCardinality(sourceArguments.Length))
            {
                return false;
            }

            var destTypes = destination.TupleElementTypesWithAnnotations;
            Debug.Assert(sourceArguments.Length == destTypes.Length);

            for (int i = 0; i < sourceArguments.Length; i++)
            {
                var sourceArgument = sourceArguments[i];
                var destType = destTypes[i];
                MakeExplicitParameterTypeInferences(binder, sourceArgument, destType, kind, ref useSiteInfo);
            }

            return true;
        }
        #endregion

        #region The second phase
        private bool InferTypeVarsSecondPhase(Binder binder, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            InitializeDependencies(DependencyKind.Function);

            while (true)
            {
                var res = DoSecondPhase(binder, ref useSiteInfo);
                Debug.Assert(res != InferenceResult.NoProgress);
                if (res == InferenceResult.InferenceFailed)
                {
                    return false;
                }
                if (res == InferenceResult.Success)
                {
                    return true;
                }
            }
        }

        private InferenceResult DoSecondPhase(Binder binder, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (AllFixed())
            {
                return InferenceResult.Success;
            }

            MakeOutputTypeInferences(binder, ref useSiteInfo);

            InferenceResult res;
            res = FixNondependentParameters(ref useSiteInfo);
            if (res != InferenceResult.NoProgress)
            {
                return res;
            }

            res = FixDependentParameters(ref useSiteInfo);
            if (res != InferenceResult.NoProgress)
            {
                return res;
            }

            return InferenceResult.InferenceFailed;
        }

        private void MakeOutputTypeInferences(Binder binder, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            for (int arg = 0, length = _sources.Length; arg < length; arg++)
            {
                var target = _targets[arg];
                var source = _sources[arg];

                MakeOutputTypeInferences(binder, source, target, ref useSiteInfo);
            }
        }

        private void MakeOutputTypeInferences(Binder binder, BoundExpression source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (source.Kind == BoundKind.TupleLiteral && (object)source.Type == null)
            {
                MakeOutputTypeInferences(binder, (BoundTupleLiteral)source, target, ref useSiteInfo);
            }
            else if (source.Kind == BoundKind.UnconvertedCollectionExpression)
            {
                MakeOutputTypeInferences(binder, (BoundUnconvertedCollectionExpression)source, target, ref useSiteInfo);
            }
            else
            {
                if (HasUnfixedParamInOutputType(source, target.Type) && !HasUnfixedParamInInputType(source, target.Type))
                {
                    OutputTypeInference(binder, source, target, ref useSiteInfo);
                }
            }
        }

        private void MakeOutputTypeInferences(Binder binder, BoundUnconvertedCollectionExpression source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (source.Elements.Length == 0)
            {
                return;
            }

            if (!binder.TryGetCollectionIterationType((ExpressionSyntax)source.Syntax, target.Type, out TypeWithAnnotations targetElementType))
            {
                return;
            }

            foreach (var element in source.Elements)
            {
                if (element is BoundExpression expression)
                {
                    MakeOutputTypeInferences(binder, expression, targetElementType, ref useSiteInfo);
                }
            }
        }


        private void MakeOutputTypeInferences(Binder binder, BoundTupleLiteral source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (target.Type.Kind != SymbolKind.NamedType)
            {
                return;
            }

            var destination = (NamedTypeSymbol)target.Type;

            Debug.Assert((object)source.Type == null, "should not need to dig into elements if tuple has natural type");
            var sourceArguments = source.Arguments;

            if (!destination.IsTupleTypeOfCardinality(sourceArguments.Length))
            {
                return;
            }

            var destTypes = destination.TupleElementTypesWithAnnotations;
            Debug.Assert(sourceArguments.Length == destTypes.Length);

            for (int i = 0; i < sourceArguments.Length; i++)
            {
                var sourceArgument = sourceArguments[i];
                var destType = destTypes[i];
                MakeOutputTypeInferences(binder, sourceArgument, destType, ref useSiteInfo);
            }
        }

        #endregion
        #endregion

        #region Bounds

        private bool ValidIndex(int index)
        {
            return 0 <= index && index < _typeVariables.Length;
        }

        private bool IsUnfixed(int typeVariableIndex)
        {
            Debug.Assert(ValidIndex(typeVariableIndex));
            return !_fixedResults[typeVariableIndex].Type.HasType;
        }

        private bool IsUnfixedTypeVariable(TypeWithAnnotations type)
        {
            Debug.Assert(type.HasType);

            if (type.TypeKind != TypeKind.TypeParameter && type.TypeKind != TypeKindInternal.InferredType)
            {
                return false;
            }

            TypeSymbol typeVar = type.Type;
            int idx = GetTypeVariableIndex(typeVar);
            return ValidIndex(idx) &&
                TypeSymbol.Equals(typeVar, _typeVariables[idx], TypeCompareKind.ConsiderEverything2) &&
                IsUnfixed(idx);
        }

        private int GetTypeVariableIndex(TypeSymbol variable)
        {
            return _typeVariables.IndexOf(variable);
        }

        private void AddBound(TypeWithAnnotations addedBound, HashSet<TypeWithAnnotations>[] collectedBounds, TypeWithAnnotations typeVariableWithAnnotations)
        {
            Debug.Assert(IsUnfixedTypeVariable(typeVariableWithAnnotations));

            var typeVariable = typeVariableWithAnnotations.Type;
            int typeVariableIndex = GetTypeVariableIndex(typeVariable);

            if (collectedBounds[typeVariableIndex] == null)
            {
                collectedBounds[typeVariableIndex] = new HashSet<TypeWithAnnotations>(TypeWithAnnotations.EqualsComparer.ConsiderEverythingComparer);
            }

            collectedBounds[typeVariableIndex].Add(addedBound);
        }

        private void AddBoundAndDoFurtherInference(TypeWithAnnotations addedBound, TypeWithAnnotations typeVariableWithAnnotations, ExactOrShapeOrBoundsKind kind, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            int varIndex = GetTypeVariableIndex(typeVariableWithAnnotations.Type);

            if (kind == ExactOrShapeOrBoundsKind.Shape)
            {
                _typeVariableShapes[varIndex] = addedBound;
            }
            else
            {
                var bounds = kind switch
                {
                    ExactOrShapeOrBoundsKind.UpperBound => _upperBounds,
                    ExactOrShapeOrBoundsKind.LowerBound => _lowerBounds,
                    ExactOrShapeOrBoundsKind.Exact => _exactBounds
                };

                if (bounds[varIndex] != null && bounds[varIndex].Contains(addedBound))
                    return;

                AddBound(addedBound, bounds, typeVariableWithAnnotations);
            }

            if (ContainsTypeVariable(addedBound))
            {
                for (int i = 0; i < _typeVariables.Length; i++)
                {
                    if (ContainsTypeVariable(addedBound.Type, _typeVariables[i]))
                    {
                        if (kind == ExactOrShapeOrBoundsKind.Shape)
                        {
                            _shapeDependencies[varIndex, i] = Dependency.Direct;
                            _shapeDependenciesDirty = true;
                        }
                        else
                        {
                            _boundDependencies[varIndex, i] = Dependency.Direct;
                            _boundDependenciesDirty = true;
                        }
                    }
                }

                DoFurtherInference(addedBound, _exactBounds[varIndex], (kind == ExactOrShapeOrBoundsKind.Shape) ? ExactOrShapeOrBoundsKind.Exact : kind, ref useSiteInfo);
                DoFurtherInference(addedBound, _lowerBounds[varIndex], ExactOrShapeOrBoundsKind.LowerBound, ref useSiteInfo);
                DoFurtherInference(addedBound, _upperBounds[varIndex], ExactOrShapeOrBoundsKind.UpperBound, ref useSiteInfo);
                if (kind != ExactOrShapeOrBoundsKind.Shape && DependsOnAny(varIndex, DependencyKind.Shape))
                    DoFurtherInferenceShape(_typeVariableShapes[varIndex], addedBound, kind, ref useSiteInfo);
            }

            if (DependsOnAny(varIndex, DependencyKind.Bound))
            {
                DoFurtherInferenceBounds(addedBound, _exactBounds[varIndex], (kind == ExactOrShapeOrBoundsKind.Shape) ? ExactOrShapeOrBoundsKind.Exact : kind, ref useSiteInfo);
                DoFurtherInferenceBounds(addedBound, _lowerBounds[varIndex], ExactOrShapeOrBoundsKind.UpperBound, ref useSiteInfo);
                DoFurtherInferenceBounds(addedBound, _upperBounds[varIndex], ExactOrShapeOrBoundsKind.LowerBound, ref useSiteInfo);
            }

            if (kind != ExactOrShapeOrBoundsKind.Shape && DependsOnAny(varIndex, DependencyKind.Shape))
            {
                DoFurtherInferenceShape(addedBound, _typeVariableShapes[varIndex], kind, ref useSiteInfo);
            }


            void DoFurtherInference(TypeWithAnnotations source, HashSet<TypeWithAnnotations> collectedBound, ExactOrShapeOrBoundsKind kind, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
            {
                if (collectedBound != null)
                {
                    foreach (var bound in collectedBound.ToList())
                    {
                        if (source.Equals(bound, TypeCompareKind.ConsiderEverything))
                            continue;
                        ExactOrBoundsInference(kind, bound, source, ref useSiteInfo);
                    }
                }
            }

            void DoFurtherInferenceBounds(TypeWithAnnotations source, HashSet<TypeWithAnnotations> collectedBound, ExactOrShapeOrBoundsKind kind, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
            {
                if (collectedBound != null)
                {
                    foreach (var bound in collectedBound.ToList())
                    {
                        if (!source.Equals(bound, TypeCompareKind.ConsiderEverything) && ContainsTypeVariable(bound))
                            ExactOrBoundsInference(kind, source, bound, ref useSiteInfo);
                    }
                }
            }

            void DoFurtherInferenceShape(TypeWithAnnotations source, TypeWithAnnotations target, ExactOrShapeOrBoundsKind kind, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
            {
                ExactOrBoundsInference(kind, source, target, ref useSiteInfo);
            }
        }

        private void UpdateBoundsAfterFix(int typeVarIdx)
        {
            var typeVar = _typeVariables[typeVarIdx];

            for (int i = 0; i < _typeVariables.Length; i++)
            {
                if (_boundDependencies[i, typeVarIdx] == Dependency.Direct)
                {
                    if (_lowerBounds[i] != null)
                        SubstituteTypeVariable(typeVar, _lowerBounds[i]);
                    if (_upperBounds[i] != null)
                        SubstituteTypeVariable(typeVar, _upperBounds[i]);
                    if (_exactBounds[i] != null)
                        SubstituteTypeVariable(typeVar, _exactBounds[i]);

                    _boundDependencies[i, typeVarIdx] = Dependency.Unknown;
                    _boundDependenciesDirty = true;
                }

                if (_shapeDependencies[i, typeVarIdx] == Dependency.Direct)
                {
                    _typeVariableShapes[i] = _map.SubstituteType(_typeVariableShapes[i]);
                    _shapeDependencies[i, typeVarIdx] = Dependency.Unknown;
                    _shapeDependenciesDirty = true;
                }
            }

            void SubstituteTypeVariable(TypeSymbol typeVar, HashSet<TypeWithAnnotations> collectedBound)
            {
                var replace = collectedBound.Where(x => ContainsTypeVariable(x.Type, typeVar)).ToList();
                collectedBound.RemoveAll(replace);
                collectedBound.AddAll(replace.Select(x => _map.SubstituteType(x)));
            }
        }

        private bool ContainsTypeVariable(TypeWithAnnotations type)
        {
            for (int i = 0; i < _typeVariables.Length; i++)
                if (ContainsTypeVariable(type.Type, _typeVariables[i])) return true;

            return false;
        }

        private static NamedTypeSymbol GetInterfaceInferenceBound(ImmutableArray<NamedTypeSymbol> interfaces, NamedTypeSymbol target)
        {
            Debug.Assert(target.IsInterface);
            NamedTypeSymbol matchingInterface = null;
            foreach (var currentInterface in interfaces)
            {
                if (TypeSymbol.Equals(currentInterface.OriginalDefinition, target.OriginalDefinition, TypeCompareKind.ConsiderEverything))
                {
                    if ((object)matchingInterface == null)
                    {
                        matchingInterface = currentInterface;
                    }
                    else if (!TypeSymbol.Equals(matchingInterface, currentInterface, TypeCompareKind.ConsiderEverything))
                    {
                        return null;
                    }
                }
            }
            return matchingInterface;
        }

        private bool AllFixed()
        {
            for (int typeVariableIndex = 0; typeVariableIndex < _typeVariables.Length; ++typeVariableIndex)
            {
                if (IsUnfixed(typeVariableIndex))
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasBound(int typeVariableIndex)
        {
            Debug.Assert(ValidIndex(typeVariableIndex));
            return (_lowerBounds[typeVariableIndex] != null && HasNonDependentBound(_lowerBounds[typeVariableIndex])) ||
                (_upperBounds[typeVariableIndex] != null && HasNonDependentBound(_upperBounds[typeVariableIndex])) ||
                (_exactBounds[typeVariableIndex] != null && HasNonDependentBound(_exactBounds[typeVariableIndex])) ||
                (!_typeVariableShapes[typeVariableIndex].IsDefault && !ContainsTypeVariable(_typeVariableShapes[typeVariableIndex]));

            bool HasNonDependentBound(HashSet<TypeWithAnnotations> collectedBound)
            {
                foreach (var item in collectedBound)
                {
                    if (!ContainsTypeVariable(item)) return true;
                }

                return false;
            }
        }

        private ImmutableArray<(TypeSymbol, TypeWithAnnotations)> GetResults(out bool inferredFromFunctionType)
        {
            for (int i = 0; i < _typeVariables.Length; i++)
            {
                var fixedResultType = _fixedResults[i].Type;
                if (fixedResultType.HasType)
                {
                    if (!fixedResultType.Type.IsErrorType())
                    {
                        if (_conversions.IncludeNullability && _nullableAnnotationLowerBounds[i].IsAnnotated())
                        {
                            _fixedResults[i] = _fixedResults[i] with { Type = fixedResultType.AsAnnotated() };
                        }
                        continue;
                    }

                    var errorTypeName = fixedResultType.Type.Name;
                    if (errorTypeName != null)
                    {
                        continue;
                    }
                }
                _fixedResults[i] = default;
            }

            return GetInferredTypeVariables(out inferredFromFunctionType);
        }

        private ImmutableArray<(TypeSymbol, TypeWithAnnotations)> GetInferredTypeVariables(out bool inferredFromFunctionType)
        {
            var builder = ArrayBuilder<(TypeSymbol, TypeWithAnnotations)>.GetInstance(_fixedResults.Length);
            inferredFromFunctionType = false;
            for (int i = 0; i < _fixedResults.Length; i++)
            {
                builder.Add((_typeVariables[i], _fixedResults[i].Type));
                if (_fixedResults[i].FromFunctionType)
                {
                    inferredFromFunctionType = true;
                }
            }
            return builder.ToImmutableAndFree();
        }
        #endregion

        #region Dependencies
        private void InitializeDependencies(DependencyKind kind)
        {
            switch (kind)
            {
                case DependencyKind.Function:
                    InitializeFunctionDependency();
                    break;
                case DependencyKind.Bound:
                    InitializeBoundDependency();
                    break;
                case DependencyKind.Shape:
                    InitializeShapeDependency();
                    break;
                default:
                    Debug.Assert(false);
                    break;
            }

            void InitializeFunctionDependency()
            {
                Debug.Assert(_functionDependencies == null);
                _functionDependencies = new Dependency[_typeVariables.Length, _typeVariables.Length];
                int iParam;
                int jParam;
                Debug.Assert(0 == (int)Dependency.Unknown);
                for (iParam = 0; iParam < _typeVariables.Length; ++iParam)
                {
                    for (jParam = 0; jParam < _typeVariables.Length; ++jParam)
                    {
                        if (DependsDirectlyOn(iParam, jParam))
                        {
                            _functionDependencies[iParam, jParam] = Dependency.Direct;
                        }
                    }
                }

                DeduceAllDependencies(DependencyKind.Function);
            }

            void InitializeBoundDependency()
            {
                Debug.Assert(_boundDependencies == null);
                _boundDependencies = new Dependency[_typeVariables.Length, _typeVariables.Length];

                for (int i = 0; i < _typeVariables.Length; i++)
                    for (int j = 0; j < _typeVariables.Length; j++)
                        _boundDependencies[i, j] = Dependency.NotDependent;
            }

            void InitializeShapeDependency()
            {
                Debug.Assert(_shapeDependencies == null);
                _shapeDependencies = new Dependency[_typeVariables.Length, _typeVariables.Length];

                for (int i = 0; i < _typeVariables.Length; i++)
                {
                    if (_typeVariableShapes[i].IsDefault || _typeVariableShapes[i] == default || !ContainsTypeVariable(_typeVariableShapes[i]))
                    {
                        for (int j = 0; j < _typeVariables.Length; j++)
                            _shapeDependencies[i, j] = Dependency.NotDependent;
                    }
                    else
                    {
                        for (int j = 0; j < _typeVariables.Length; j++)
                            _shapeDependencies[i, j] = (ContainsTypeVariable(_typeVariableShapes[i].Type, _typeVariables[j]))
                                ? Dependency.Direct
                                : Dependency.Unknown;
                    }
                }

                DeduceAllDependencies(DependencyKind.Shape);
            }
        }

        private bool DependsDirectlyOn(int iParam, int jParam)
        {
            Debug.Assert(ValidIndex(iParam));
            Debug.Assert(ValidIndex(jParam));

            Debug.Assert(IsUnfixed(iParam));
            Debug.Assert(IsUnfixed(jParam));

            for (int iArg = 0, length = _sources.Length; iArg < length; iArg++)
            {
                var targetType = _targets[iArg].Type;
                var source = _sources[iArg];
                if (DoesInputTypeContain(source, targetType, _typeVariables[jParam]) &&
                    DoesOutputTypeContain(source, targetType, _typeVariables[iParam]))
                {
                    return true;
                }
            }
            return false;
        }

        private void DeduceAllDependencies(DependencyKind kind)
        {
            ref bool dirtyFlag = ref GetDirtyFlag(kind);

            bool madeProgress;
            do
            {
                madeProgress = DeduceDependencies(kind);
            } while (madeProgress);
            SetUnknownsToNotDependent(kind);
            dirtyFlag = false;
        }

        private bool DeduceDependencies(DependencyKind kind)
        {
            var dependencies = GetDependencies(kind);

            Debug.Assert(dependencies != null);
            bool madeProgress = false;
            for (int iParam = 0; iParam < _typeVariables.Length; ++iParam)
            {
                for (int jParam = 0; jParam < _typeVariables.Length; ++jParam)
                {
                    if (dependencies[iParam, jParam] == Dependency.Unknown)
                    {
                        if (DependsTransitivelyOn(iParam, jParam, kind))
                        {
                            dependencies[iParam, jParam] = Dependency.Indirect;
                            madeProgress = true;
                        }
                    }
                }
            }
            return madeProgress;
        }

        private bool DependsTransitivelyOn(int iParam, int jParam, DependencyKind kind)
        {
            var dependencies = GetDependencies(kind);

            Debug.Assert(dependencies != null);
            Debug.Assert(ValidIndex(iParam));
            Debug.Assert(ValidIndex(jParam));

            for (int kParam = 0; kParam < _typeVariables.Length; ++kParam)
            {
                if (((dependencies[iParam, kParam]) & Dependency.DependsMask) != 0 &&
                    ((dependencies[kParam, jParam]) & Dependency.DependsMask) != 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void SetUnknownsToNotDependent(DependencyKind kind)
        {
            var dependencies = GetDependencies(kind);

            Debug.Assert(dependencies != null);
            for (int iParam = 0; iParam < _typeVariables.Length; ++iParam)
            {
                for (int jParam = 0; jParam < _typeVariables.Length; ++jParam)
                {
                    if (dependencies[iParam, jParam] == Dependency.Unknown)
                    {
                        dependencies[iParam, jParam] = Dependency.NotDependent;
                    }
                }
            }
        }

        private bool DependsOn(int iParam, int jParam, DependencyKind kind)
        {
            var dependencies = GetDependencies(kind);
            ref bool dirtyFlag = ref GetDirtyFlag(kind);

            Debug.Assert(dependencies != null);
            Debug.Assert(0 <= iParam && iParam < _typeVariables.Length);
            Debug.Assert(0 <= jParam && jParam < _typeVariables.Length);

            if (dirtyFlag)
            {
                SetIndirectsToUnknown(kind);
                DeduceAllDependencies(kind);
            }
            return 0 != ((dependencies[iParam, jParam]) & Dependency.DependsMask);
        }

        private void SetIndirectsToUnknown(DependencyKind kind)
        {
            var dependencies = GetDependencies(kind);

            Debug.Assert(dependencies != null);
            for (int iParam = 0; iParam < _typeVariables.Length; ++iParam)
            {
                for (int jParam = 0; jParam < _typeVariables.Length; ++jParam)
                {
                    if (dependencies[iParam, jParam] == Dependency.Indirect)
                    {
                        dependencies[iParam, jParam] = Dependency.Unknown;
                    }
                }
            }
        }

        private bool DependsOnAny(int iParam, DependencyKind kind)
        {
            Debug.Assert(ValidIndex(iParam));
            for (int jParam = 0; jParam < _typeVariables.Length; ++jParam)
            {
                if (DependsOn(iParam, jParam, kind))
                {
                    return true;
                }
            }
            return false;
        }

        private bool AnyDependsOn(int iParam, DependencyKind kind)
        {
            Debug.Assert(ValidIndex(iParam));
            for (int jParam = 0; jParam < _typeVariables.Length; ++jParam)
            {
                if (DependsOn(jParam, iParam, kind))
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateDependenciesAfterFix(int iParam, DependencyKind kind)
        {
            var dependencies = GetDependencies(kind);
            ref bool dirtyFlag = ref GetDirtyFlag(kind);

            Debug.Assert(ValidIndex(iParam));
            if (dependencies == null)
            {
                return;
            }
            for (int jParam = 0; jParam < _typeVariables.Length; ++jParam)
            {
                dependencies[iParam, jParam] = Dependency.NotDependent;
                dependencies[jParam, iParam] = Dependency.NotDependent;
            }
            dirtyFlag = true;
        }

        private ref bool GetDirtyFlag(DependencyKind kind)
        {
            switch (kind)
            {
                case DependencyKind.Function: return ref _functionDependenciesDirty;
                case DependencyKind.Bound: return ref _boundDependenciesDirty;
                case DependencyKind.Shape: return ref _shapeDependenciesDirty;
                default:
                    Debug.Assert(false);
                    throw new Exception();
            }
        }

        private Dependency[,] GetDependencies(DependencyKind kind)
        {
            switch (kind)
            {
                case DependencyKind.Function: return _functionDependencies;
                case DependencyKind.Bound: return _boundDependencies;
                case DependencyKind.Shape: return _shapeDependencies;
                default:
                    Debug.Assert(false);
                    throw new Exception();
            }
        }
        #endregion

        #region Input types
        private static bool DoesInputTypeContain(BoundExpression source, TypeSymbol target, TypeSymbol typeVariable)
        {
            var delegateOrFunctionPointerType = target.GetDelegateOrFunctionPointerType();
            if ((object)delegateOrFunctionPointerType == null)
            {
                return false;
            }

            var isFunctionPointer = delegateOrFunctionPointerType.IsFunctionPointer();
            if ((isFunctionPointer && source.Kind != BoundKind.UnconvertedAddressOfOperator) ||
                (!isFunctionPointer && source.Kind is not (BoundKind.UnboundLambda or BoundKind.MethodGroup)))
            {
                return false;
            }

            var parameters = delegateOrFunctionPointerType.DelegateOrFunctionPointerParameters();
            if (parameters.IsDefaultOrEmpty)
            {
                return false;
            }

            foreach (var parameter in parameters)
            {
                if (ContainsTypeVariable(parameter.Type, typeVariable))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasUnfixedParamInInputType(BoundExpression pSource, TypeSymbol pDest)
        {
            for (int iParam = 0; iParam < _typeVariables.Length; iParam++)
            {
                if (IsUnfixed(iParam))
                {
                    if (DoesInputTypeContain(pSource, pDest, _typeVariables[iParam]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool ContainsTypeVariable(TypeSymbol symbol, TypeSymbol typeVariable)
        {
            RoslynDebug.Assert((object)symbol != null);

            var type = (TypeSymbol)typeVariable;
            var result = symbol.VisitType(static (t, l, _) => (t.TypeKind == TypeKind.TypeParameter || t.TypeKind == TypeKindInternal.InferredType) && (l is null || TypeSymbol.Equals(t, l, TypeCompareKind.ConsiderEverything2)), type);
            return result is object;
        }
        #endregion

        #region Output types
        private static bool DoesOutputTypeContain(BoundExpression source, TypeSymbol target, TypeSymbol typeVariable)
        {
            var delegateOrFunctionPointerType = target.GetDelegateOrFunctionPointerType();
            if ((object)delegateOrFunctionPointerType == null)
            {
                return false;
            }

            var isFunctionPointer = delegateOrFunctionPointerType.IsFunctionPointer();
            if ((isFunctionPointer && source.Kind != BoundKind.UnconvertedAddressOfOperator) ||
                (!isFunctionPointer && source.Kind is not (BoundKind.UnboundLambda or BoundKind.MethodGroup)))
            {
                return false;
            }

            MethodSymbol method = delegateOrFunctionPointerType switch
            {
                NamedTypeSymbol n => n.DelegateInvokeMethod,
                FunctionPointerTypeSymbol f => f.Signature,
                _ => throw ExceptionUtilities.UnexpectedValue(delegateOrFunctionPointerType)
            };

            if ((object)method == null || method.HasUseSiteError)
            {
                return false;
            }

            var returnType = method.ReturnType;
            if ((object)returnType == null)
            {
                return false;
            }

            return ContainsTypeVariable(returnType, typeVariable);
        }

        private bool HasUnfixedParamInOutputType(BoundExpression source, TypeSymbol target)
        {
            for (int iParam = 0; iParam < _typeVariables.Length; iParam++)
            {
                if (IsUnfixed(iParam))
                {
                    if (DoesOutputTypeContain(source, target, _typeVariables[iParam]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        #endregion

        #region Output type inferences
        private void OutputTypeInference(Binder binder, BoundExpression expression, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(expression != null);
            Debug.Assert(target.HasType);

            if (InferredReturnTypeInference(expression, target, ref useSiteInfo))
            {
                return;
            }

            if (MethodGroupReturnTypeInference(binder, expression, target.Type, ref useSiteInfo))
            {
                return;
            }

            var sourceType = _extensions.GetTypeWithAnnotations(expression);
            if (sourceType.HasType)
            {
                LowerBoundInference(sourceType, target, ref useSiteInfo);
            }
        }

        private bool InferredReturnTypeInference(BoundExpression source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source != null);
            Debug.Assert(target.HasType);

            var delegateType = target.Type.GetDelegateType();
            if ((object)delegateType == null)
            {
                return false;
            }

            Debug.Assert((object)delegateType.DelegateInvokeMethod != null && !delegateType.DelegateInvokeMethod.HasUseSiteError,
                         "This method should only be called for valid delegate types.");
            var returnType = delegateType.DelegateInvokeMethod.ReturnTypeWithAnnotations;
            if (!returnType.HasType || returnType.SpecialType == SpecialType.System_Void)
            {
                return false;
            }

            var inferredReturnType = InferReturnType(source, delegateType, ref useSiteInfo);
            if (!inferredReturnType.HasType)
            {
                return false;
            }

            Debug.Assert(inferredReturnType.Type is not FunctionTypeSymbol);

            LowerBoundInference(inferredReturnType, returnType, ref useSiteInfo);
            return true;
        }

        private bool MethodGroupReturnTypeInference(Binder binder, BoundExpression source, TypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source != null);
            Debug.Assert((object)target != null);

            if (source.Kind is not (BoundKind.MethodGroup or BoundKind.UnconvertedAddressOfOperator))
            {
                return false;
            }

            var delegateOrFunctionPointerType = target.GetDelegateOrFunctionPointerType();
            if ((object)delegateOrFunctionPointerType == null)
            {
                return false;
            }

            if (delegateOrFunctionPointerType.IsFunctionPointer() != (source.Kind == BoundKind.UnconvertedAddressOfOperator))
            {
                return false;
            }

            var (method, isFunctionPointerResolution) = delegateOrFunctionPointerType switch
            {
                NamedTypeSymbol n => (n.DelegateInvokeMethod, false),
                FunctionPointerTypeSymbol f => (f.Signature, true),
                _ => throw ExceptionUtilities.UnexpectedValue(delegateOrFunctionPointerType),
            };
            Debug.Assert(method is { HasUseSiteError: false },
                         "This method should only be called for valid delegate or function pointer types");

            TypeWithAnnotations sourceReturnType = method.ReturnTypeWithAnnotations;
            if (!sourceReturnType.HasType || sourceReturnType.SpecialType == SpecialType.System_Void)
            {
                return false;
            }

            var fixedParameters = GetFixedDelegateOrFunctionPointer(delegateOrFunctionPointerType).DelegateOrFunctionPointerParameters();
            if (fixedParameters.IsDefault)
            {
                return false;
            }

            CallingConventionInfo callingConventionInfo = isFunctionPointerResolution
                ? new CallingConventionInfo(method.CallingConvention, ((FunctionPointerMethodSymbol)method).GetCallingConventionModifiers())
                : default;
            BoundMethodGroup originalMethodGroup = source as BoundMethodGroup ?? ((BoundUnconvertedAddressOfOperator)source).Operand;

            var returnType = MethodGroupReturnType(binder, originalMethodGroup, fixedParameters, method.RefKind, isFunctionPointerResolution, ref useSiteInfo, in callingConventionInfo);
            if (returnType.IsDefault || returnType.IsVoidType())
            {
                return false;
            }

            LowerBoundInference(returnType, sourceReturnType, ref useSiteInfo);
            return true;
        }

        private TypeWithAnnotations MethodGroupReturnType(
            Binder binder, BoundMethodGroup source,
            ImmutableArray<ParameterSymbol> delegateParameters,
            RefKind delegateRefKind,
            bool isFunctionPointerResolution,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo,
            in CallingConventionInfo callingConventionInfo)
        {
            var analyzedArguments = AnalyzedArguments.GetInstance();
            Conversions.GetDelegateOrFunctionPointerArguments(source.Syntax, analyzedArguments, delegateParameters, binder.Compilation);

            var resolution = binder.ResolveMethodGroup(source, analyzedArguments, useSiteInfo: ref useSiteInfo,
                isMethodGroupConversion: true, returnRefKind: delegateRefKind,
                returnType: null,
                isFunctionPointerResolution: isFunctionPointerResolution, callingConventionInfo: in callingConventionInfo);

            TypeWithAnnotations type = default;

            if (!resolution.IsEmpty)
            {
                var result = resolution.OverloadResolutionResult;
                if (result.Succeeded)
                {
                    type = _extensions.GetMethodGroupResultType(source, result.BestResult.Member);
                }
            }

            analyzedArguments.Free();
            resolution.Free();
            return type;
        }
        #endregion

        #region Explicit type inferences
        private void ExplicitParameterTypeInference(BoundExpression source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source != null);
            Debug.Assert(target.HasType);

            if (source.Kind != BoundKind.UnboundLambda)
            {
                return;
            }

            UnboundLambda anonymousFunction = (UnboundLambda)source;

            if (!anonymousFunction.HasExplicitlyTypedParameterList)
            {
                return;
            }

            var delegateType = target.Type.GetDelegateType();
            if (delegateType is null)
            {
                return;
            }

            var delegateParameters = delegateType.DelegateParameters();
            if (delegateParameters.IsDefault)
            {
                return;
            }

            int size = delegateParameters.Length;
            if (anonymousFunction.ParameterCount < size)
            {
                size = anonymousFunction.ParameterCount;
            }

            for (int i = 0; i < size; ++i)
            {
                ExactInference(anonymousFunction.ParameterTypeWithAnnotations(i), delegateParameters[i].TypeWithAnnotations, ref useSiteInfo);
            }
        }

        private void ExplicitReturnTypeInference(BoundExpression source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source != null);
            Debug.Assert(target.HasType);

            if (source.Kind != BoundKind.UnboundLambda)
            {
                return;
            }

            UnboundLambda anonymousFunction = (UnboundLambda)source;
            if (!anonymousFunction.HasExplicitReturnType(out _, out TypeWithAnnotations anonymousFunctionReturnType))
            {
                return;
            }

            var delegateInvokeMethod = target.Type.GetDelegateType()?.DelegateInvokeMethod();
            if (delegateInvokeMethod is null)
            {
                return;
            }

            ExactInference(anonymousFunctionReturnType, delegateInvokeMethod.ReturnTypeWithAnnotations, ref useSiteInfo);
        }
        #endregion

        #region Exact inferences
        private void ExactInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            if (ExactNullableInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (ExactTypeVariableInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (ExactArrayInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (ExactConstructedInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (ExactPointerInference(source, target, ref useSiteInfo))
            {
                return;
            }
        }

        private bool ExactTypeVariableInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            if (IsUnfixedTypeVariable(target))
            {
                AddBoundAndDoFurtherInference(source, target, ExactOrShapeOrBoundsKind.Exact, ref useSiteInfo);
                return true;
            }
            return false;
        }

        private bool ExactArrayInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            if (!source.Type.IsArray() || !target.Type.IsArray())
            {
                return false;
            }

            var arraySource = (ArrayTypeSymbol)source.Type;
            var arrayTarget = (ArrayTypeSymbol)target.Type;
            if (!arraySource.HasSameShapeAs(arrayTarget))
            {
                return false;
            }

            ExactInference(arraySource.ElementTypeWithAnnotations, arrayTarget.ElementTypeWithAnnotations, ref useSiteInfo);
            return true;
        }

        private bool ExactNullableInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            return ExactOrBoundsNullableInference(ExactOrShapeOrBoundsKind.Exact, source, target, ref useSiteInfo);
        }

        private bool ExactConstructedInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            var namedSource = source.Type as NamedTypeSymbol;
            if ((object)namedSource == null)
            {
                return false;
            }

            var namedTarget = target.Type as NamedTypeSymbol;
            if ((object)namedTarget == null)
            {
                return false;
            }

            if (!TypeSymbol.Equals(namedSource.OriginalDefinition, namedTarget.OriginalDefinition, TypeCompareKind.ConsiderEverything2))
            {
                return false;
            }

            ExactTypeArgumentInference(namedSource, namedTarget, ref useSiteInfo);
            return true;
        }

        private bool ExactPointerInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (source.TypeKind == TypeKind.Pointer && target.TypeKind == TypeKind.Pointer)
            {
                ExactInference(((PointerTypeSymbol)source.Type).PointedAtTypeWithAnnotations, ((PointerTypeSymbol)target.Type).PointedAtTypeWithAnnotations, ref useSiteInfo);
                return true;
            }
            else if (source.Type is FunctionPointerTypeSymbol { Signature: { ParameterCount: int sourceParameterCount } sourceSignature } &&
                     target.Type is FunctionPointerTypeSymbol { Signature: { ParameterCount: int targetParameterCount } targetSignature } &&
                     sourceParameterCount == targetParameterCount)
            {
                if (!FunctionPointerRefKindsEqual(sourceSignature, targetSignature) || !FunctionPointerCallingConventionsEqual(sourceSignature, targetSignature))
                {
                    return false;
                }

                for (int i = 0; i < sourceParameterCount; i++)
                {
                    ExactInference(sourceSignature.ParameterTypesWithAnnotations[i], targetSignature.ParameterTypesWithAnnotations[i], ref useSiteInfo);
                }

                ExactInference(sourceSignature.ReturnTypeWithAnnotations, targetSignature.ReturnTypeWithAnnotations, ref useSiteInfo);
                return true;
            }

            return false;
        }

        private static bool FunctionPointerCallingConventionsEqual(FunctionPointerMethodSymbol sourceSignature, FunctionPointerMethodSymbol targetSignature)
        {
            if (sourceSignature.CallingConvention != targetSignature.CallingConvention)
            {
                return false;
            }

            return (sourceSignature.GetCallingConventionModifiers(), targetSignature.GetCallingConventionModifiers()) switch
            {
                (null, null) => true,
                ({ } sourceModifiers, { } targetModifiers) when sourceModifiers.SetEquals(targetModifiers) => true,
                _ => false
            };
        }

        private static bool FunctionPointerRefKindsEqual(FunctionPointerMethodSymbol sourceSignature, FunctionPointerMethodSymbol targetSignature)
        {
            return sourceSignature.RefKind == targetSignature.RefKind
                   && (sourceSignature.ParameterRefKinds.IsDefault, targetSignature.ParameterRefKinds.IsDefault) switch
                   {
                       (true, false) or (false, true) => false,
                       (true, true) => true,
                       _ => sourceSignature.ParameterRefKinds.SequenceEqual(targetSignature.ParameterRefKinds)
                   };
        }

        private void ExactTypeArgumentInference(NamedTypeSymbol source, NamedTypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);
            Debug.Assert(TypeSymbol.Equals(source.OriginalDefinition, target.OriginalDefinition, TypeCompareKind.ConsiderEverything2));

            var sourceTypeArguments = ArrayBuilder<TypeWithAnnotations>.GetInstance();
            var targetTypeArguments = ArrayBuilder<TypeWithAnnotations>.GetInstance();

            source.GetAllTypeArguments(sourceTypeArguments, ref useSiteInfo);
            target.GetAllTypeArguments(targetTypeArguments, ref useSiteInfo);

            Debug.Assert(sourceTypeArguments.Count == targetTypeArguments.Count);

            for (int arg = 0; arg < sourceTypeArguments.Count; ++arg)
            {
                ExactInference(sourceTypeArguments[arg], targetTypeArguments[arg], ref useSiteInfo);
            }

            sourceTypeArguments.Free();
            targetTypeArguments.Free();
        }

        private void ExactOrBoundsInference(ExactOrShapeOrBoundsKind kind, TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            switch (kind)
            {
                case ExactOrShapeOrBoundsKind.Exact:
                    ExactInference(source, target, ref useSiteInfo);
                    break;
                case ExactOrShapeOrBoundsKind.LowerBound:
                    LowerBoundInference(source, target, ref useSiteInfo);
                    break;
                case ExactOrShapeOrBoundsKind.UpperBound:
                    UpperBoundInference(source, target, ref useSiteInfo);
                    break;
            }
        }
        private bool ExactOrBoundsNullableInference(ExactOrShapeOrBoundsKind kind, TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            if (source.IsNullableType() && target.IsNullableType())
            {
                ExactOrBoundsInference(kind, ((NamedTypeSymbol)source.Type).TypeArgumentsWithAnnotationsNoUseSiteDiagnostics[0], ((NamedTypeSymbol)target.Type).TypeArgumentsWithAnnotationsNoUseSiteDiagnostics[0], ref useSiteInfo);
                return true;
            }

            if (isNullableOnly(source) && isNullableOnly(target))
            {
                ExactOrBoundsInference(kind, source.AsNotNullableReferenceType(), target.AsNotNullableReferenceType(), ref useSiteInfo);
                return true;
            }

            return false;

            // True if the type is nullable.
            static bool isNullableOnly(TypeWithAnnotations type)
                => type.NullableAnnotation.IsAnnotated();
        }
        #endregion

        #region Lower-bound inferences
        private void LowerBoundInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            if (LowerBoundNullableInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (LowerBoundTypeVariableInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (LowerBoundArrayInference(source.Type, target.Type, ref useSiteInfo))
            {
                return;
            }

            if (LowerBoundTupleInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (LowerBoundConstructedInference(source.Type, target.Type, ref useSiteInfo))
            {
                return;
            }

            if (LowerBoundFunctionPointerTypeInference(source.Type, target.Type, ref useSiteInfo))
            {
                return;
            }
        }

        private bool LowerBoundTupleInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            ImmutableArray<TypeWithAnnotations> sourceTypes;
            ImmutableArray<TypeWithAnnotations> targetTypes;

            if (!source.Type.TryGetElementTypesWithAnnotationsIfTupleType(out sourceTypes) ||
                !target.Type.TryGetElementTypesWithAnnotationsIfTupleType(out targetTypes) ||
                sourceTypes.Length != targetTypes.Length)
            {
                return false;
            }

            for (int i = 0; i < sourceTypes.Length; i++)
            {
                LowerBoundInference(sourceTypes[i], targetTypes[i], ref useSiteInfo);
            }

            return true;
        }

        private bool LowerBoundTypeVariableInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            if (IsUnfixedTypeVariable(target))
            {
                AddBoundAndDoFurtherInference(source, target, ExactOrShapeOrBoundsKind.LowerBound, ref useSiteInfo);
                return true;
            }
            return false;
        }

        private static TypeWithAnnotations GetMatchingElementType(ArrayTypeSymbol source, TypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);

            if (target.IsArray())
            {
                var arrayTarget = (ArrayTypeSymbol)target;
                if (!arrayTarget.HasSameShapeAs(source))
                {
                    return default;
                }
                return arrayTarget.ElementTypeWithAnnotations;
            }

            if (!source.IsSZArray)
            {
                return default;
            }

            if (!target.IsPossibleArrayGenericInterface())
            {
                return default;
            }

            return ((NamedTypeSymbol)target).TypeArgumentWithDefinitionUseSiteDiagnostics(0, ref useSiteInfo);
        }

        private bool LowerBoundArrayInference(TypeSymbol source, TypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);

            if (!source.IsArray())
            {
                return false;
            }

            var arraySource = (ArrayTypeSymbol)source;
            var elementSource = arraySource.ElementTypeWithAnnotations;
            var elementTarget = GetMatchingElementType(arraySource, target, ref useSiteInfo);
            if (!elementTarget.HasType)
            {
                return false;
            }

            if (elementSource.Type.IsReferenceType)
            {
                LowerBoundInference(elementSource, elementTarget, ref useSiteInfo);
            }
            else
            {
                ExactInference(elementSource, elementTarget, ref useSiteInfo);
            }

            return true;
        }

        private bool LowerBoundNullableInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            return ExactOrBoundsNullableInference(ExactOrShapeOrBoundsKind.LowerBound, source, target, ref useSiteInfo);
        }

        private bool LowerBoundConstructedInference(TypeSymbol source, TypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);

            var constructedTarget = target as NamedTypeSymbol;
            if ((object)constructedTarget == null)
            {
                return false;
            }

            if (constructedTarget.AllTypeArgumentCount() == 0)
            {
                return false;
            }

            var constructedSource = source as NamedTypeSymbol;
            if ((object)constructedSource != null &&
                TypeSymbol.Equals(constructedSource.OriginalDefinition, constructedTarget.OriginalDefinition, TypeCompareKind.ConsiderEverything2))
            {
                if (constructedSource.IsInterface || constructedSource.IsDelegateType())
                {
                    LowerBoundTypeArgumentInference(constructedSource, constructedTarget, ref useSiteInfo);
                }
                else
                {
                    ExactTypeArgumentInference(constructedSource, constructedTarget, ref useSiteInfo);
                }
                return true;
            }

            if (LowerBoundClassInference(source, constructedTarget, ref useSiteInfo))
            {
                return true;
            }

            if (LowerBoundInterfaceInference(source, constructedTarget, ref useSiteInfo))
            {
                return true;
            }

            return false;
        }

        private bool LowerBoundClassInference(TypeSymbol source, NamedTypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);

            if (target.TypeKind != TypeKind.Class)
            {
                return false;
            }

            NamedTypeSymbol sourceBase = null;

            if (source.TypeKind == TypeKind.Class)
            {
                sourceBase = source.BaseTypeWithDefinitionUseSiteDiagnostics(ref useSiteInfo);
            }
            else if (source.TypeKind == TypeKind.TypeParameter)
            {
                sourceBase = ((TypeParameterSymbol)source).EffectiveBaseClass(ref useSiteInfo);
            }

            while ((object)sourceBase != null)
            {
                if (TypeSymbol.Equals(sourceBase.OriginalDefinition, target.OriginalDefinition, TypeCompareKind.ConsiderEverything2))
                {
                    ExactTypeArgumentInference(sourceBase, target, ref useSiteInfo);
                    return true;
                }
                sourceBase = sourceBase.BaseTypeWithDefinitionUseSiteDiagnostics(ref useSiteInfo);
            }
            return false;
        }

        private bool LowerBoundInterfaceInference(TypeSymbol source, NamedTypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);

            if (!target.IsInterface)
            {
                return false;
            }

            ImmutableArray<NamedTypeSymbol> allInterfaces;
            switch (source.TypeKind)
            {
                case TypeKind.Struct:
                case TypeKind.Class:
                case TypeKind.Interface:
                    allInterfaces = source.AllInterfacesWithDefinitionUseSiteDiagnostics(ref useSiteInfo);
                    break;

                case TypeKind.TypeParameter:
                    var typeParameter = (TypeParameterSymbol)source;
                    allInterfaces = typeParameter.EffectiveBaseClass(ref useSiteInfo).
                                        AllInterfacesWithDefinitionUseSiteDiagnostics(ref useSiteInfo).
                                        Concat(typeParameter.AllEffectiveInterfacesWithDefinitionUseSiteDiagnostics(ref useSiteInfo));
                    break;

                default:
                    return false;
            }

            allInterfaces = ModuloReferenceTypeNullabilityDifferences(allInterfaces, VarianceKind.In);

            NamedTypeSymbol matchingInterface = GetInterfaceInferenceBound(allInterfaces, target);
            if ((object)matchingInterface == null)
            {
                return false;
            }
            LowerBoundTypeArgumentInference(matchingInterface, target, ref useSiteInfo);
            return true;
        }

        internal static ImmutableArray<NamedTypeSymbol> ModuloReferenceTypeNullabilityDifferences(ImmutableArray<NamedTypeSymbol> interfaces, VarianceKind variance)
        {
            var dictionary = PooledDictionaryIgnoringNullableModifiersForReferenceTypes.GetInstance();

            foreach (var @interface in interfaces)
            {
                if (dictionary.TryGetValue(@interface, out var found))
                {
                    var merged = (NamedTypeSymbol)found.MergeEquivalentTypes(@interface, variance);
                    dictionary[@interface] = merged;
                }
                else
                {
                    dictionary.Add(@interface, @interface);
                }
            }

            var result = dictionary.Count != interfaces.Length ? dictionary.Values.ToImmutableArray() : interfaces;
            dictionary.Free();
            return result;
        }

        private void LowerBoundTypeArgumentInference(NamedTypeSymbol source, NamedTypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);
            Debug.Assert(TypeSymbol.Equals(source.OriginalDefinition, target.OriginalDefinition, TypeCompareKind.ConsiderEverything2));

            var typeParameters = ArrayBuilder<TypeParameterSymbol>.GetInstance();
            var sourceTypeArguments = ArrayBuilder<TypeWithAnnotations>.GetInstance();
            var targetTypeArguments = ArrayBuilder<TypeWithAnnotations>.GetInstance();

            source.OriginalDefinition.GetAllTypeParameters(typeParameters);
            source.GetAllTypeArguments(sourceTypeArguments, ref useSiteInfo);
            target.GetAllTypeArguments(targetTypeArguments, ref useSiteInfo);

            Debug.Assert(typeParameters.Count == sourceTypeArguments.Count);
            Debug.Assert(typeParameters.Count == targetTypeArguments.Count);

            for (int arg = 0; arg < sourceTypeArguments.Count; ++arg)
            {
                var typeParameter = typeParameters[arg];
                var sourceTypeArgument = sourceTypeArguments[arg];
                var targetTypeArgument = targetTypeArguments[arg];

                if (sourceTypeArgument.Type.IsReferenceType && typeParameter.Variance == VarianceKind.Out)
                {
                    LowerBoundInference(sourceTypeArgument, targetTypeArgument, ref useSiteInfo);
                }
                else if (sourceTypeArgument.Type.IsReferenceType && typeParameter.Variance == VarianceKind.In)
                {
                    UpperBoundInference(sourceTypeArgument, targetTypeArgument, ref useSiteInfo);
                }
                else
                {
                    ExactInference(sourceTypeArgument, targetTypeArgument, ref useSiteInfo);
                }
            }

            typeParameters.Free();
            sourceTypeArguments.Free();
            targetTypeArguments.Free();
        }

#nullable enable
        private bool LowerBoundFunctionPointerTypeInference(TypeSymbol source, TypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (source is not FunctionPointerTypeSymbol { Signature: { } sourceSignature } || target is not FunctionPointerTypeSymbol { Signature: { } targetSignature })
            {
                return false;
            }

            if (sourceSignature.ParameterCount != targetSignature.ParameterCount)
            {
                return false;
            }

            if (!FunctionPointerRefKindsEqual(sourceSignature, targetSignature) || !FunctionPointerCallingConventionsEqual(sourceSignature, targetSignature))
            {
                return false;
            }

            // Reference parameters are treated as "input" variance by default, and reference return types are treated as out variance by default.
            // If they have a ref kind or are not reference types, then they are treated as invariant.
            for (int i = 0; i < sourceSignature.ParameterCount; i++)
            {
                var sourceParam = sourceSignature.Parameters[i];
                var targetParam = targetSignature.Parameters[i];

                if ((sourceParam.Type.IsReferenceType || sourceParam.Type.IsFunctionPointer()) && sourceParam.RefKind == RefKind.None)
                {
                    UpperBoundInference(sourceParam.TypeWithAnnotations, targetParam.TypeWithAnnotations, ref useSiteInfo);
                }
                else
                {
                    ExactInference(sourceParam.TypeWithAnnotations, targetParam.TypeWithAnnotations, ref useSiteInfo);
                }
            }

            if ((sourceSignature.ReturnType.IsReferenceType || sourceSignature.ReturnType.IsFunctionPointer()) && sourceSignature.RefKind == RefKind.None)
            {
                LowerBoundInference(sourceSignature.ReturnTypeWithAnnotations, targetSignature.ReturnTypeWithAnnotations, ref useSiteInfo);
            }
            else
            {
                ExactInference(sourceSignature.ReturnTypeWithAnnotations, targetSignature.ReturnTypeWithAnnotations, ref useSiteInfo);
            }

            return true;
        }
#nullable disable
        #endregion

        #region Upper-bound inferences
        private void UpperBoundInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            if (UpperBoundNullableInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (UpperBoundTypeVariableInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (UpperBoundArrayInference(source, target, ref useSiteInfo))
            {
                return;
            }


            if (UpperBoundConstructedInference(source, target, ref useSiteInfo))
            {
                return;
            }

            if (UpperBoundFunctionPointerTypeInference(source.Type, target.Type, ref useSiteInfo))
            {
                return;
            }
        }

        private bool UpperBoundTypeVariableInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            if (IsUnfixedTypeVariable(target))
            {
                AddBoundAndDoFurtherInference(source, target, ExactOrShapeOrBoundsKind.UpperBound, ref useSiteInfo);
                return true;
            }
            return false;
        }

        private bool UpperBoundArrayInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(source.HasType);
            Debug.Assert(target.HasType);

            if (!target.Type.IsArray())
            {
                return false;
            }
            var arrayTarget = (ArrayTypeSymbol)target.Type;
            var elementTarget = arrayTarget.ElementTypeWithAnnotations;
            var elementSource = GetMatchingElementType(arrayTarget, source.Type, ref useSiteInfo);
            if (!elementSource.HasType)
            {
                return false;
            }

            if (elementSource.Type.IsReferenceType)
            {
                UpperBoundInference(elementSource, elementTarget, ref useSiteInfo);
            }
            else
            {
                ExactInference(elementSource, elementTarget, ref useSiteInfo);
            }

            return true;
        }

        private bool UpperBoundNullableInference(TypeWithAnnotations source, TypeWithAnnotations target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            return ExactOrBoundsNullableInference(ExactOrShapeOrBoundsKind.UpperBound, source, target, ref useSiteInfo);
        }

        private bool UpperBoundConstructedInference(TypeWithAnnotations sourceWithAnnotations, TypeWithAnnotations targetWithAnnotations, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(sourceWithAnnotations.HasType);
            Debug.Assert(targetWithAnnotations.HasType);
            var source = sourceWithAnnotations.Type;
            var target = targetWithAnnotations.Type;

            var constructedSource = source as NamedTypeSymbol;
            if ((object)constructedSource == null)
            {
                return false;
            }

            if (constructedSource.AllTypeArgumentCount() == 0)
            {
                return false;
            }

            var constructedTarget = target as NamedTypeSymbol;

            if ((object)constructedTarget != null &&
                TypeSymbol.Equals(constructedSource.OriginalDefinition, target.OriginalDefinition, TypeCompareKind.ConsiderEverything2))
            {
                if (constructedTarget.IsInterface || constructedTarget.IsDelegateType())
                {
                    UpperBoundTypeArgumentInference(constructedSource, constructedTarget, ref useSiteInfo);
                }
                else
                {
                    ExactTypeArgumentInference(constructedSource, constructedTarget, ref useSiteInfo);
                }
                return true;
            }

            if (UpperBoundClassInference(constructedSource, target, ref useSiteInfo))
            {
                return true;
            }

            if (UpperBoundInterfaceInference(constructedSource, target, ref useSiteInfo))
            {
                return true;
            }

            return false;
        }

        private bool UpperBoundClassInference(NamedTypeSymbol source, TypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);

            if (source.TypeKind != TypeKind.Class || target.TypeKind != TypeKind.Class)
            {
                return false;
            }

            var targetBase = target.BaseTypeWithDefinitionUseSiteDiagnostics(ref useSiteInfo);
            while ((object)targetBase != null)
            {
                if (TypeSymbol.Equals(targetBase.OriginalDefinition, source.OriginalDefinition, TypeCompareKind.ConsiderEverything2))
                {
                    ExactTypeArgumentInference(source, targetBase, ref useSiteInfo);
                    return true;
                }

                targetBase = targetBase.BaseTypeWithDefinitionUseSiteDiagnostics(ref useSiteInfo);
            }

            return false;
        }

        private bool UpperBoundInterfaceInference(NamedTypeSymbol source, TypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);

            if (!source.IsInterface)
            {
                return false;
            }

            switch (target.TypeKind)
            {
                case TypeKind.Struct:
                case TypeKind.Class:
                case TypeKind.Interface:
                    break;

                default:
                    return false;
            }

            ImmutableArray<NamedTypeSymbol> allInterfaces = target.AllInterfacesWithDefinitionUseSiteDiagnostics(ref useSiteInfo);

            allInterfaces = ModuloReferenceTypeNullabilityDifferences(allInterfaces, VarianceKind.Out);

            NamedTypeSymbol bestInterface = GetInterfaceInferenceBound(allInterfaces, source);
            if ((object)bestInterface == null)
            {
                return false;
            }

            UpperBoundTypeArgumentInference(source, bestInterface, ref useSiteInfo);
            return true;
        }

        private void UpperBoundTypeArgumentInference(NamedTypeSymbol source, NamedTypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)source != null);
            Debug.Assert((object)target != null);
            Debug.Assert(TypeSymbol.Equals(source.OriginalDefinition, target.OriginalDefinition, TypeCompareKind.ConsiderEverything2));

            var typeParameters = ArrayBuilder<TypeParameterSymbol>.GetInstance();
            var sourceTypeArguments = ArrayBuilder<TypeWithAnnotations>.GetInstance();
            var targetTypeArguments = ArrayBuilder<TypeWithAnnotations>.GetInstance();

            source.OriginalDefinition.GetAllTypeParameters(typeParameters);
            source.GetAllTypeArguments(sourceTypeArguments, ref useSiteInfo);
            target.GetAllTypeArguments(targetTypeArguments, ref useSiteInfo);

            Debug.Assert(typeParameters.Count == sourceTypeArguments.Count);
            Debug.Assert(typeParameters.Count == targetTypeArguments.Count);

            for (int arg = 0; arg < sourceTypeArguments.Count; ++arg)
            {
                var typeParameter = typeParameters[arg];
                var sourceTypeArgument = sourceTypeArguments[arg];
                var targetTypeArgument = targetTypeArguments[arg];

                if (sourceTypeArgument.Type.IsReferenceType && typeParameter.Variance == VarianceKind.Out)
                {
                    UpperBoundInference(sourceTypeArgument, targetTypeArgument, ref useSiteInfo);
                }
                else if (sourceTypeArgument.Type.IsReferenceType && typeParameter.Variance == VarianceKind.In)
                {
                    LowerBoundInference(sourceTypeArgument, targetTypeArgument, ref useSiteInfo);
                }
                else
                {
                    ExactInference(sourceTypeArgument, targetTypeArgument, ref useSiteInfo);
                }
            }

            typeParameters.Free();
            sourceTypeArguments.Free();
            targetTypeArguments.Free();
        }

#nullable enable
        private bool UpperBoundFunctionPointerTypeInference(TypeSymbol source, TypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            if (source is not FunctionPointerTypeSymbol { Signature: { } sourceSignature } || target is not FunctionPointerTypeSymbol { Signature: { } targetSignature })
            {
                return false;
            }

            if (sourceSignature.ParameterCount != targetSignature.ParameterCount)
            {
                return false;
            }

            if (!FunctionPointerRefKindsEqual(sourceSignature, targetSignature) || !FunctionPointerCallingConventionsEqual(sourceSignature, targetSignature))
            {
                return false;
            }

            // Reference parameters are treated as "input" variance by default, and reference return types are treated as out variance by default.
            // If they have a ref kind or are not reference types, then they are treated as invariant.
            for (int i = 0; i < sourceSignature.ParameterCount; i++)
            {
                var sourceParam = sourceSignature.Parameters[i];
                var targetParam = targetSignature.Parameters[i];

                if ((sourceParam.Type.IsReferenceType || sourceParam.Type.IsFunctionPointer()) && sourceParam.RefKind == RefKind.None)
                {
                    LowerBoundInference(sourceParam.TypeWithAnnotations, targetParam.TypeWithAnnotations, ref useSiteInfo);
                }
                else
                {
                    ExactInference(sourceParam.TypeWithAnnotations, targetParam.TypeWithAnnotations, ref useSiteInfo);
                }
            }

            if ((sourceSignature.ReturnType.IsReferenceType || sourceSignature.ReturnType.IsFunctionPointer()) && sourceSignature.RefKind == RefKind.None)
            {
                UpperBoundInference(sourceSignature.ReturnTypeWithAnnotations, targetSignature.ReturnTypeWithAnnotations, ref useSiteInfo);
            }
            else
            {
                ExactInference(sourceSignature.ReturnTypeWithAnnotations, targetSignature.ReturnTypeWithAnnotations, ref useSiteInfo);
            }

            return true;
        }
#nullable disable
        #endregion

        #region Return type inferences
        private TypeWithAnnotations InferReturnType(BoundExpression source, NamedTypeSymbol target, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert((object)target != null);
            Debug.Assert(target.IsDelegateType());
            Debug.Assert((object)target.DelegateInvokeMethod != null && !target.DelegateInvokeMethod.HasUseSiteError,
                            "This method should only be called for legal delegate types.");
            Debug.Assert(!target.DelegateInvokeMethod.ReturnsVoid);

            Debug.Assert(!HasUnfixedParamInInputType(source, target));

            if (source.Kind != BoundKind.UnboundLambda)
            {
                return default;
            }

            var anonymousFunction = (UnboundLambda)source;
            if (anonymousFunction.HasSignature)
            {

                var originalDelegateParameters = target.DelegateParameters();
                if (originalDelegateParameters.IsDefault)
                {
                    return default;
                }

                if (originalDelegateParameters.Length != anonymousFunction.ParameterCount)
                {
                    return default;
                }
            }

            var fixedDelegate = (NamedTypeSymbol)GetFixedDelegateOrFunctionPointer(target);
            var fixedDelegateParameters = fixedDelegate.DelegateParameters();

            if (anonymousFunction.HasExplicitlyTypedParameterList)
            {
                for (int p = 0; p < anonymousFunction.ParameterCount; ++p)
                {
                    if (!anonymousFunction.ParameterType(p).Equals(fixedDelegateParameters[p].Type, TypeCompareKind.IgnoreDynamicAndTupleNames | TypeCompareKind.IgnoreNullableModifiersForReferenceTypes))
                    {
                        return default;
                    }
                }
            }

            var returnType = anonymousFunction.InferReturnType(_conversions, fixedDelegate, ref useSiteInfo, out bool inferredFromFunctionType);
            if (inferredFromFunctionType)
            {
                return default;
            }
            return returnType;
        }
        #endregion

        #region Fixing
        private InferenceResult FixNondependentParameters(ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            return FixParameters((inferrer, index) => !inferrer.DependsOnAny(index, DependencyKind.Function) && !inferrer.DependsOnAny(index, DependencyKind.Shape) && !inferrer.DependsOnAny(index, DependencyKind.Bound), ref useSiteInfo);
        }

        private InferenceResult FixDependentParameters(ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            return FixParameters((inferrer, index) => !inferrer.DependsOnAny(index, DependencyKind.Shape) && (inferrer.AnyDependsOn(index, DependencyKind.Shape) || inferrer.AnyDependsOn(index, DependencyKind.Function) || inferrer.AnyDependsOn(index, DependencyKind.Bound)), ref useSiteInfo);
        }

        private InferenceResult FixParameters(
            Func<TypeInferrer, int, bool> predicate,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            var needsFixing = new bool[_typeVariables.Length];
            var result = InferenceResult.NoProgress;
            for (int param = 0; param < _typeVariables.Length; param++)
            {
                if (IsUnfixed(param) && HasBound(param) && predicate(this, param))
                {
                    needsFixing[param] = true;
                    result = InferenceResult.MadeProgress;
                }
            }

            for (int param = 0; param < _typeVariables.Length; param++)
            {
                if (needsFixing[param])
                {
                    if (!Fix(param, ref useSiteInfo))
                    {
                        result = InferenceResult.InferenceFailed;
                    }
                }
            }
            return result;
        }

#nullable enable
        private bool Fix(int iParam, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo)
        {
            Debug.Assert(IsUnfixed(iParam));

            var typeVariable = _typeVariables[iParam];
            var exact = _exactBounds[iParam];
            var lower = _lowerBounds[iParam];
            var upper = _upperBounds[iParam];

            var best = Fix(_compilation, _conversions, typeVariable, _typeVariableShapes[iParam], exact, lower, upper, ref useSiteInfo, new HashSet<TypeParameterSymbol>(_typeVariables.Where(x => x.Kind == SymbolKind.TypeParameter).Cast<TypeParameterSymbol>()));
            if (!best.Type.HasType)
            {
                return false;
            }

#if DEBUG
            if (_conversions.IncludeNullability)
            {
                var discardedUseSiteInfo = CompoundUseSiteInfo<AssemblySymbol>.Discarded;
                var withoutNullability = Fix(_compilation, _conversions.WithNullability(false), typeVariable, _typeVariableShapes[iParam], exact, lower, upper, ref discardedUseSiteInfo, new HashSet<TypeParameterSymbol>(_typeVariables.Where(x => x.Kind == SymbolKind.TypeParameter).Cast<TypeParameterSymbol>())).Type;

                Debug.Assert(best.Type.Type.Equals(withoutNullability.Type, TypeCompareKind.IgnoreDynamicAndTupleNames | TypeCompareKind.IgnoreNullableModifiersForReferenceTypes));
            }
#endif

            _fixedResults[iParam] = best;
            _map.Add(_typeVariables[iParam], best.Type);
            UpdateBoundsAfterFix(iParam);
            UpdateDependenciesAfterFix(iParam, DependencyKind.Function);
            UpdateDependenciesAfterFix(iParam, DependencyKind.Bound);
            return true;
        }

        private static (TypeWithAnnotations Type, bool FromFunctionType) Fix(
           CSharpCompilation compilation,
           ConversionsBase conversions,
           TypeSymbol typeVariable,
           TypeWithAnnotations shape,
           HashSet<TypeWithAnnotations>? exact,
           HashSet<TypeWithAnnotations>? lower,
           HashSet<TypeWithAnnotations>? upper,
           ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo,
           HashSet<TypeParameterSymbol> typeParameters)
        {
            if (!shape.IsDefault)
            {
                exact ??= new HashSet<TypeWithAnnotations>();
                exact.Add(shape);
            }

            var candidates = new Dictionary<TypeWithAnnotations, TypeWithAnnotations>(EqualsIgnoringDynamicTupleNamesAndNullabilityComparer.Instance);

            Debug.Assert(!containsFunctionTypes(exact));
            Debug.Assert(!containsFunctionTypes(upper));

            if (containsFunctionTypes(lower) &&
                (containsNonFunctionTypes(lower) || containsNonFunctionTypes(exact) || containsNonFunctionTypes(upper)))
            {
                lower = removeTypes(lower, static type => isFunctionType(type, out _));
            }

            lower = removeTypes(lower, static type => isFunctionType(type, out var functionType) && functionType.GetInternalDelegateType() is null);
            lower = removeTypes(lower, type => type.Type.IsInferred() || type.Type.ContainsTypeParameters(typeParameters));
            exact = removeTypes(exact, type => type.Type.IsInferred() || type.Type.ContainsTypeParameters(typeParameters));
            upper = removeTypes(upper, type => type.Type.IsInferred() || type.Type.ContainsTypeParameters(typeParameters));

            if (!shape.IsDefault)
            {
                candidates.Add(shape, shape);
            }
            else if (exact == null)
            {
                if (lower != null)
                {
                    AddAllCandidates(candidates, lower, VarianceKind.Out, conversions);
                }
                if (upper != null)
                {
                    AddAllCandidates(candidates, upper, VarianceKind.In, conversions);
                }
            }
            else
            {
                AddAllCandidates(candidates, exact, VarianceKind.None, conversions);
                if (candidates.Count >= 2)
                {
                    return default;
                }
            }

            if (candidates.Count == 0)
            {
                return default;
            }

            var initialCandidates = ArrayBuilder<TypeWithAnnotations>.GetInstance();
            GetAllCandidates(candidates, initialCandidates);

            if (lower != null)
            {
                MergeOrRemoveCandidates(candidates, lower, initialCandidates, conversions, VarianceKind.Out, ref useSiteInfo);
            }

            if (upper != null)
            {
                MergeOrRemoveCandidates(candidates, upper, initialCandidates, conversions, VarianceKind.In, ref useSiteInfo);
            }
            if (!shape.IsDefault)
            {
                var shapeSet = new HashSet<TypeWithAnnotations> { shape };
                initialCandidates.Clear();
                initialCandidates.AddRange(candidates.Select(x => x.Value));
                MergeOrRemoveCandidates(candidates, shapeSet, initialCandidates, conversions, VarianceKind.In, ref useSiteInfo, withNullability: true);
            }

            initialCandidates.Clear();
            GetAllCandidates(candidates, initialCandidates);

            TypeWithAnnotations best = default;
            foreach (var candidate in initialCandidates)
            {
                foreach (var candidate2 in initialCandidates)
                {
                    if (!candidate.Equals(candidate2, TypeCompareKind.ConsiderEverything) &&
                        !ImplicitConversionExists(candidate2, candidate, ref useSiteInfo, conversions.WithNullability(false)))
                    {
                        goto OuterBreak;
                    }
                }

                if (!best.HasType)
                {
                    best = candidate;
                }
                else
                {
                    Debug.Assert(!best.Equals(candidate, TypeCompareKind.IgnoreDynamicAndTupleNames | TypeCompareKind.IgnoreNullableModifiersForReferenceTypes));
                    best = default;
                    break;
                }

OuterBreak:
                ;
            }

            initialCandidates.Free();

            bool fromFunctionType = false;
            if (isFunctionType(best, out var functionType))
            {
                var resultType = functionType.GetInternalDelegateType();
                var symbol = (TypeSymbol)typeVariable;
                if (symbol.TypeKind == TypeKind.TypeParameter && hasExpressionTypeConstraint((TypeParameterSymbol)typeVariable))
                {
                    var expressionOfTType = compilation.GetWellKnownType(WellKnownType.System_Linq_Expressions_Expression_T);
                    resultType = expressionOfTType.Construct(resultType);
                }
                best = TypeWithAnnotations.Create(resultType, best.NullableAnnotation);
                fromFunctionType = true;
            }

            return (best, fromFunctionType);

            static bool containsFunctionTypes([NotNullWhen(true)] HashSet<TypeWithAnnotations>? types)
            {
                return types?.Any(t => isFunctionType(t, out _)) == true;
            }

            static bool containsNonFunctionTypes([NotNullWhen(true)] HashSet<TypeWithAnnotations>? types)
            {
                return types?.Any(t => !isFunctionType(t, out _)) == true;
            }

            static bool isFunctionType(TypeWithAnnotations type, [NotNullWhen(true)] out FunctionTypeSymbol? functionType)
            {
                functionType = type.Type as FunctionTypeSymbol;
                return functionType is not null;
            }

            static bool hasExpressionTypeConstraint(TypeParameterSymbol typeParameter)
            {
                var constraintTypes = typeParameter.ConstraintTypesNoUseSiteDiagnostics;
                return constraintTypes.Any(static t => isExpressionType(t.Type));
            }

            static bool isExpressionType(TypeSymbol? type)
            {
                while (type is { })
                {
                    if (type.IsGenericOrNonGenericExpressionType(out _))
                    {
                        return true;
                    }
                    type = type.BaseTypeNoUseSiteDiagnostics;
                }
                return false;
            }

            static HashSet<TypeWithAnnotations>? removeTypes(HashSet<TypeWithAnnotations>? types, Func<TypeWithAnnotations, bool> predicate)
            {
                if (types is null)
                {
                    return null;
                }
                HashSet<TypeWithAnnotations>? updated = null;
                foreach (var type in types)
                {
                    if (!predicate(type))
                    {
                        updated ??= new HashSet<TypeWithAnnotations>(TypeWithAnnotations.EqualsComparer.ConsiderEverythingComparer);
                        updated.Add(type);
                    }
                }
                return updated;
            }
        }

        private static bool ImplicitConversionExists(TypeWithAnnotations sourceWithAnnotations, TypeWithAnnotations destinationWithAnnotations, ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo, ConversionsBase conversions)
        {
            var source = sourceWithAnnotations.Type;
            var destination = destinationWithAnnotations.Type;

            // SPEC VIOLATION: For the purpose of algorithm in Fix method, dynamic type is not considered convertible to any other type, including object.
            if (source.IsDynamic() && !destination.IsDynamic())
            {
                return false;
            }

            if (!conversions.HasTopLevelNullabilityImplicitConversion(sourceWithAnnotations, destinationWithAnnotations))
            {
                return false;
            }

            return conversions.ClassifyImplicitConversionFromTypeWhenNeitherOrBothFunctionTypes(source, destination, ref useSiteInfo).Exists;
        }
#nullable disable
        private static void GetAllCandidates(Dictionary<TypeWithAnnotations, TypeWithAnnotations> candidates, ArrayBuilder<TypeWithAnnotations> builder)
        {
            builder.AddRange(candidates.Values);
        }

        private static void AddAllCandidates(
            Dictionary<TypeWithAnnotations, TypeWithAnnotations> candidates,
            HashSet<TypeWithAnnotations> bounds,
            VarianceKind variance,
            ConversionsBase conversions)
        {
            foreach (var candidate in bounds)
            {
                var type = candidate;
                if (!conversions.IncludeNullability)
                {
                    type = type.SetUnknownNullabilityForReferenceTypes();
                }

                Debug.Assert(conversions.IncludeNullability ||
                    type.SetUnknownNullabilityForReferenceTypes().Equals(type, TypeCompareKind.ConsiderEverything));

                AddOrMergeCandidate(candidates, type, variance);
            }
        }

        private static void AddOrMergeCandidate(
            Dictionary<TypeWithAnnotations, TypeWithAnnotations> candidates,
            TypeWithAnnotations newCandidate,
            VarianceKind variance)
        {
            if (candidates.TryGetValue(newCandidate, out TypeWithAnnotations oldCandidate))
            {
                MergeAndReplaceIfStillCandidate(candidates, oldCandidate, newCandidate, variance);
            }
            else
            {
                candidates.Add(newCandidate, newCandidate);
            }
        }

        private static void MergeOrRemoveCandidates(
            Dictionary<TypeWithAnnotations, TypeWithAnnotations> candidates,
            HashSet<TypeWithAnnotations> bounds,
            ArrayBuilder<TypeWithAnnotations> initialCandidates,
            ConversionsBase conversions,
            VarianceKind variance,
            ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo,
            bool withNullability = false)
        {
            Debug.Assert(variance == VarianceKind.In || variance == VarianceKind.Out);
            var comparison = conversions.IncludeNullability ? TypeCompareKind.ConsiderEverything : TypeCompareKind.IgnoreNullableModifiersForReferenceTypes;
            foreach (var bound in bounds)
            {
                foreach (var candidate in initialCandidates)
                {
                    if (bound.Equals(candidate, comparison))
                    {
                        continue;
                    }
                    TypeWithAnnotations source;
                    TypeWithAnnotations destination;
                    if (variance == VarianceKind.Out)
                    {
                        source = bound;
                        destination = candidate;
                    }
                    else
                    {
                        source = candidate;
                        destination = bound;
                    }
                    if (!ImplicitConversionExists(source, destination, ref useSiteInfo, conversions.WithNullability(withNullability)))
                    {
                        candidates.Remove(candidate);
                        if (conversions.IncludeNullability && candidates.TryGetValue(bound, out var oldBound))
                        {
                            var oldAnnotation = oldBound.NullableAnnotation;
                            var newAnnotation = oldAnnotation.MergeNullableAnnotation(candidate.NullableAnnotation, variance);
                            if (oldAnnotation != newAnnotation)
                            {
                                var newBound = TypeWithAnnotations.Create(oldBound.Type, newAnnotation);
                                candidates[bound] = newBound;
                            }
                        }
                    }
                    else if (bound.Equals(candidate, TypeCompareKind.IgnoreDynamicAndTupleNames | TypeCompareKind.IgnoreNullableModifiersForReferenceTypes))
                    {
                        MergeAndReplaceIfStillCandidate(candidates, candidate, bound, variance);
                    }
                }
            }
        }

        private static void MergeAndReplaceIfStillCandidate(
            Dictionary<TypeWithAnnotations, TypeWithAnnotations> candidates,
            TypeWithAnnotations oldCandidate,
            TypeWithAnnotations newCandidate,
            VarianceKind variance)
        {
            if (newCandidate.Type.IsDynamic())
            {
                return;
            }

            if (candidates.TryGetValue(oldCandidate, out TypeWithAnnotations latest))
            {
                TypeWithAnnotations merged = latest.MergeEquivalentTypes(newCandidate, variance);
                candidates[oldCandidate] = merged;
            }
        }
        #endregion

        #region OtherHelpers
        private static bool IsReallyAType(TypeSymbol? type)
        {
            return type is { } &&
                !type.IsErrorType() &&
                !type.IsVoidType();
        }

        private TypeSymbol GetFixedDelegateOrFunctionPointer(TypeSymbol delegateOrFunctionPointerType)
        {
            Debug.Assert((object)delegateOrFunctionPointerType != null);
            Debug.Assert(delegateOrFunctionPointerType.IsDelegateType() || delegateOrFunctionPointerType is FunctionPointerTypeSymbol);

            var typeParameters = _typeVariables
                .Select(static (item, idx) => (item, idx))
                .Where(static x => ((TypeSymbol)x.item).TypeKind == TypeKind.TypeParameter)
                .ToImmutableArray();

            var fixedArguments = typeParameters
                .SelectAsArray(
                static (item, self) => self.IsUnfixed(item.idx) ? TypeWithAnnotations.Create((TypeSymbol)item.item) : self._fixedResults[item.idx].Type,
                this);

            TypeMap typeMap = new TypeMap(_constructedContainingType, typeParameters.SelectAsArray(static (item, self) => (TypeParameterSymbol)item.item, this), fixedArguments);
            return typeMap.SubstituteType(delegateOrFunctionPointerType).Type;
        }
        #endregion
    }
}
