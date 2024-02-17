// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    internal class RenamedTypeParameterSymbol : WrappedTypeParameterSymbol
    {
        public RenamedTypeParameterSymbol(TypeParameterSymbol underlyingTypeParameter) : base(underlyingTypeParameter)
        {
        }

        public override Symbol ContainingSymbol => _underlyingTypeParameter.ContainingSymbol;

        internal override bool? IsNotNullable => _underlyingTypeParameter.IsNotNullable;

        internal override ImmutableArray<TypeWithAnnotations> GetConstraintTypes(ConsList<TypeParameterSymbol> inProgress)
        {
            return _underlyingTypeParameter.GetConstraintTypes(inProgress);
        }

        internal override TypeSymbol GetDeducedBaseType(ConsList<TypeParameterSymbol> inProgress)
        {
            return _underlyingTypeParameter.GetDeducedBaseType(inProgress);
        }

        internal override NamedTypeSymbol GetEffectiveBaseClass(ConsList<TypeParameterSymbol> inProgress)
        {
            return _underlyingTypeParameter.GetEffectiveBaseClass(inProgress);
        }

        internal override ImmutableArray<NamedTypeSymbol> GetInterfaces(ConsList<TypeParameterSymbol> inProgress)
        {
            return _underlyingTypeParameter.GetInterfaces(inProgress);
        }
    }
}
