// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    internal sealed class SourceInferredTypeSymbol : TypeSymbol
    {
        public SyntaxNode Syntax { get; init; }

        public SourceInferredTypeSymbol(SyntaxNode syntax)
        {
            Syntax = syntax;
        }

        public override bool IsReferenceType => throw ExceptionUtilities.Unreachable();

        public override bool IsValueType => throw ExceptionUtilities.Unreachable();

        public override TypeKind TypeKind => TypeKindInternal.InferredType;

        public override bool IsRefLikeType => throw ExceptionUtilities.Unreachable();

        public override bool IsReadOnly => throw ExceptionUtilities.Unreachable();

        public override SymbolKind Kind => SymbolKindInternal.InferredType;

        public override Symbol ContainingSymbol => throw ExceptionUtilities.Unreachable();

        public override ImmutableArray<Location> Locations => throw new NotImplementedException();

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => ImmutableArray.Create(Syntax.GetReference());

        public override Accessibility DeclaredAccessibility => throw ExceptionUtilities.Unreachable();

        public override bool IsStatic => throw ExceptionUtilities.Unreachable();

        public override bool IsAbstract => throw ExceptionUtilities.Unreachable();

        public override bool IsSealed => throw ExceptionUtilities.Unreachable();

        internal override NamedTypeSymbol BaseTypeNoUseSiteDiagnostics => throw new NotImplementedException();

        internal override bool IsRecord => throw ExceptionUtilities.Unreachable();

        internal override bool IsRecordStruct => throw ExceptionUtilities.Unreachable();

        internal override ObsoleteAttributeData? ObsoleteAttributeData => throw ExceptionUtilities.Unreachable();

        public override void Accept(CSharpSymbolVisitor visitor) => throw ExceptionUtilities.Unreachable();

        public override TResult Accept<TResult>(CSharpSymbolVisitor<TResult> visitor) => throw ExceptionUtilities.Unreachable();

        public override ImmutableArray<Symbol> GetMembers() => throw ExceptionUtilities.Unreachable();

        public override ImmutableArray<Symbol> GetMembers(string name) => throw ExceptionUtilities.Unreachable();

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers() => throw ExceptionUtilities.Unreachable();

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(ReadOnlyMemory<char> name) => throw ExceptionUtilities.Unreachable();

        protected override ISymbol CreateISymbol() => throw ExceptionUtilities.Unreachable();

        protected override ITypeSymbol CreateITypeSymbol(CodeAnalysis.NullableAnnotation nullableAnnotation) => throw ExceptionUtilities.Unreachable();

        internal override TResult Accept<TArgument, TResult>(CSharpSymbolVisitor<TArgument, TResult> visitor, TArgument a) => throw ExceptionUtilities.Unreachable();

        internal override void AddNullableTransforms(ArrayBuilder<byte> transforms) => throw ExceptionUtilities.Unreachable();

        internal override bool ApplyNullableTransforms(byte defaultTransformFlag, ImmutableArray<byte> transforms, ref int position, out TypeSymbol result) => throw ExceptionUtilities.Unreachable();

        internal override ManagedKind GetManagedKind(ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo) => throw ExceptionUtilities.Unreachable();

        internal override bool GetUnificationUseSiteDiagnosticRecursive(ref DiagnosticInfo result, Symbol owner, ref HashSet<TypeSymbol> checkedTypes) => throw ExceptionUtilities.Unreachable();

        internal override bool HasInlineArrayAttribute(out int length) => throw ExceptionUtilities.Unreachable();

        internal override ImmutableArray<NamedTypeSymbol> InterfacesNoUseSiteDiagnostics(ConsList<TypeSymbol> basesBeingResolved = null) => throw ExceptionUtilities.Unreachable();

        internal override TypeSymbol MergeEquivalentTypes(TypeSymbol other, VarianceKind variance) => throw ExceptionUtilities.Unreachable();

        internal override TypeSymbol SetNullabilityForReferenceTypes(Func<TypeWithAnnotations, TypeWithAnnotations> transform) => throw ExceptionUtilities.Unreachable();

        internal override IEnumerable<(MethodSymbol Body, MethodSymbol Implemented)> SynthesizedInterfaceMethodImpls() => throw ExceptionUtilities.Unreachable();

        internal override string GetDebuggerDisplay()
        {
            return $"InferredType _";
        }
    }
}
