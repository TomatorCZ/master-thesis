// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using ReferenceEqualityComparer = Roslyn.Utilities.ReferenceEqualityComparer;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    [DebuggerDisplay("{GetDebuggerDisplay(), nq}")]
    internal sealed class TypeVariableMap : AbstractTypeMap
    {
        private readonly SmallDictionary<TypeSymbol, TypeWithAnnotations> Mapping;

        private TypeVariableMap(SmallDictionary<TypeSymbol, TypeWithAnnotations> mapping)
        {
            Mapping = mapping;
        }

        internal TypeVariableMap(ImmutableArray<TypeSymbol> from, ImmutableArray<TypeWithAnnotations> to)
            : this(ConstructMapping(from, to))
        { }

        internal TypeVariableMap()
            : this(new SmallDictionary<TypeSymbol, TypeWithAnnotations>(ReferenceEqualityComparer.Instance))
        { }

        private static SmallDictionary<TypeSymbol, TypeWithAnnotations> ConstructMapping(ImmutableArray<TypeSymbol> from, ImmutableArray<TypeWithAnnotations> to)
        {
            var mapping = new SmallDictionary<TypeSymbol, TypeWithAnnotations>(ReferenceEqualityComparer.Instance);

            Debug.Assert(from.Length == to.Length);

            for (int i = 0; i < from.Length; i++)
            {
                TypeSymbol tp = from[i];
                TypeWithAnnotations ta = to[i];
                if ((tp is TypeParameterSymbol { } tpp && !ta.Is(tpp))
                 || (tp is SourceInferredTypeSymbol { } tpb && !ta.Is(tpb)))
                {
                    mapping.Add(tp, ta);
                }
            }

            return mapping;
        }

        internal void Add(TypeSymbol key, TypeWithAnnotations value)
        {
            if ((key is TypeParameterSymbol { } tpp && !value.Is(tpp))
             || (key is SourceInferredTypeSymbol { } tpb && !value.Is(tpb)))
            {
                Mapping.Add(key, value);
            }
        }

        protected sealed override TypeWithAnnotations SubstituteTypeParameter(TypeParameterSymbol typeParameter)
        {
            TypeWithAnnotations result;
            if (Mapping.TryGetValue(typeParameter, out result))
            {
                return result;
            }

            return TypeWithAnnotations.Create(typeParameter);
        }

        protected override TypeWithAnnotations SubstituteInferredType(SourceInferredTypeSymbol typeArgument)
        {
            TypeWithAnnotations result;
            if (Mapping.TryGetValue(typeArgument, out result))
            {
                return result;
            }

            return TypeWithAnnotations.Create(typeArgument);
        }

        private string GetDebuggerDisplay()
        {
            var result = new StringBuilder("[");
            result.Append(this.GetType().Name);
            foreach (var kv in Mapping)
            {
                result.Append(" ").Append(kv.Key).Append(":").Append(kv.Value.Type);
            }

            return result.Append("]").ToString();
        }
    }
}
