using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ApexPack.Generator;

/// <summary>
/// [ApexPackable] 型を解析して <see cref="TypeModel"/> を作る。型からフォーマッタ式への対応もここに置く
/// (実行時の ApexFormatterResolver と同じ対応を保つこと)。
/// </summary>
internal sealed class TypeAnalyzer
{
    private const string Ns = "global::ApexPack.";

    private static readonly SymbolDisplayFormat FullName = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private readonly Compilation _compilation;
    private readonly List<Diagnostic> _diagnostics;
    private readonly Dictionary<INamedTypeSymbol, TypeModel?> _cache = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> _inProgress = new(SymbolEqualityComparer.Default);
    private readonly INamedTypeSymbol? _packableInterface;

    public TypeAnalyzer(Compilation compilation, List<Diagnostic> diagnostics)
    {
        _compilation = compilation;
        _diagnostics = diagnostics;
        _packableInterface = compilation.GetTypeByMetadataName("ApexPack.ISerializePackable`1");
    }

    public static string Name(ITypeSymbol type) => type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(FullName);

    // ====================================================================
    // 型の分類
    // ====================================================================

    public static AttributeData? GetPackableAttribute(ISymbol symbol)
        => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "ApexPack.ApexPackableAttribute");

    private static bool HasAttribute(ISymbol symbol, string fullName)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fullName);

    private static AttributeData? GetAttribute(ISymbol symbol, string fullName)
        => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fullName);

    /// <summary>[ApexPackable](このコンパイル内で生成中)または ISerializePackable&lt;T&gt; 実装済み(参照先)なら true。</summary>
    public bool IsPackable(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return false;
        if (GetPackableAttribute(named) is not null) return true;
        if (_packableInterface is null) return false;
        foreach (var i in named.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, _packableInterface)
                && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], named))
            {
                return true;
            }
        }
        return false;
    }

    private static (int Size, bool Raw)? ScalarInfo(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return (1, true);
            case SpecialType.System_Byte:
            case SpecialType.System_SByte: return (1, true);
            case SpecialType.System_Char:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16: return (2, true);
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Single: return (4, true);
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Double:
            case SpecialType.System_DateTime: return (8, true);
            case SpecialType.System_Decimal: return (16, true);
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr: return (8, false);
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol e && e.EnumUnderlyingType is { } underlying)
        {
            var info = ScalarInfo(underlying);
            return info is null ? null : (info.Value.Size, true);
        }

        switch (type.OriginalDefinition.ToDisplayString())
        {
            case "System.Guid": return (16, true);
            case "System.TimeSpan": return (8, true);
            case "System.DateTimeOffset": return (10, false);
            case "System.DateOnly": return (4, true);
            case "System.TimeOnly": return (8, true);
            case "System.Half": return (2, true);
            case "System.Int128":
            case "System.UInt128": return (16, true);
            case "System.Text.Rune": return (4, true);
            case "System.Numerics.Complex": return (16, true);
            case "System.Numerics.Vector2": return (8, true);
            case "System.Numerics.Vector3": return (12, true);
            case "System.Numerics.Vector4":
            case "System.Numerics.Quaternion":
            case "System.Numerics.Plane": return (16, true);
            case "System.Numerics.Matrix3x2": return (24, true);
            case "System.Numerics.Matrix4x4": return (64, true);
        }
        return null;
    }

    /// <summary>固定長で表せる型なら情報を返す(スカラー、またはこのコンパイル内の固定長 [ApexPackable] 構造体)。</summary>
    public FixedInfo? GetFixed(ITypeSymbol type)
    {
        var scalar = ScalarInfo(type);
        if (scalar is not null)
        {
            return new FixedInfo { TypeName = Name(type), Size = scalar.Value.Size, IsRaw = scalar.Value.Raw };
        }

        if (type is INamedTypeSymbol named && named.IsValueType && GetPackableAttribute(named) is not null
            && named.DeclaringSyntaxReferences.Length > 0 && !named.IsGenericType)
        {
            var model = Analyze(named, report: false);
            if (model is { Kind: PackKind.FixedStruct })
            {
                return new FixedInfo { TypeName = Name(type), Size = model.FixedBytes, IsPackable = true, IsRaw = model.FixedRawEligible };
            }
        }
        return null;
    }

    private static readonly Dictionary<string, string> OneArg = new()
    {
        ["System.Collections.Generic.List<T>"] = "ListFormatter",
        ["System.Memory<T>"] = "MemoryFormatter",
        ["System.ReadOnlyMemory<T>"] = "ReadOnlyMemoryFormatter",
        ["System.ArraySegment<T>"] = "ArraySegmentFormatter",
        ["System.Buffers.ReadOnlySequence<T>"] = "ReadOnlySequenceFormatter",
        ["System.Collections.Generic.LinkedList<T>"] = "LinkedListFormatter",
        ["System.Collections.Generic.Queue<T>"] = "QueueFormatter",
        ["System.Collections.Generic.Stack<T>"] = "StackFormatter",
        ["System.Collections.Generic.HashSet<T>"] = "HashSetFormatter",
        ["System.Collections.Generic.SortedSet<T>"] = "SortedSetFormatter",
        ["System.Collections.ObjectModel.Collection<T>"] = "CollectionFormatter",
        ["System.Collections.ObjectModel.ReadOnlyCollection<T>"] = "ReadOnlyCollectionFormatter",
        ["System.Collections.ObjectModel.ObservableCollection<T>"] = "ObservableCollectionFormatter",
        ["System.Collections.ObjectModel.ReadOnlyObservableCollection<T>"] = "ReadOnlyObservableCollectionFormatter",
        ["System.Collections.Concurrent.ConcurrentQueue<T>"] = "ConcurrentQueueFormatter",
        ["System.Collections.Concurrent.ConcurrentStack<T>"] = "ConcurrentStackFormatter",
        ["System.Collections.Concurrent.ConcurrentBag<T>"] = "ConcurrentBagFormatter",
        ["System.Collections.Concurrent.BlockingCollection<T>"] = "BlockingCollectionFormatter",
        ["System.Collections.Generic.IEnumerable<out T>"] = "EnumerableFormatter",
        ["System.Collections.Generic.IReadOnlyCollection<out T>"] = "ReadOnlyCollectionInterfaceFormatter",
        ["System.Collections.Generic.IReadOnlyList<out T>"] = "ReadOnlyListInterfaceFormatter",
        ["System.Collections.Generic.ICollection<T>"] = "CollectionInterfaceFormatter",
        ["System.Collections.Generic.IList<T>"] = "ListInterfaceFormatter",
        ["System.Collections.Generic.ISet<T>"] = "SetInterfaceFormatter",
        ["System.Collections.Generic.IReadOnlySet<T>"] = "ReadOnlySetInterfaceFormatter",
        ["System.Collections.Immutable.ImmutableArray<T>"] = "ImmutableArrayFormatter",
        ["System.Collections.Immutable.ImmutableList<T>"] = "ImmutableListFormatter",
        ["System.Collections.Immutable.ImmutableQueue<T>"] = "ImmutableQueueFormatter",
        ["System.Collections.Immutable.ImmutableStack<T>"] = "ImmutableStackFormatter",
        ["System.Collections.Immutable.ImmutableHashSet<T>"] = "ImmutableHashSetFormatter",
        ["System.Collections.Immutable.ImmutableSortedSet<T>"] = "ImmutableSortedSetFormatter",
        ["System.Collections.Immutable.IImmutableList<T>"] = "ImmutableListInterfaceFormatter",
        ["System.Collections.Immutable.IImmutableQueue<T>"] = "ImmutableQueueInterfaceFormatter",
        ["System.Collections.Immutable.IImmutableStack<T>"] = "ImmutableStackInterfaceFormatter",
        ["System.Collections.Immutable.IImmutableSet<T>"] = "ImmutableSetInterfaceFormatter",
        ["System.Lazy<T>"] = "LazyFormatter",
    };

    private static readonly Dictionary<string, string> TwoArg = new()
    {
        ["System.Collections.Generic.Dictionary<TKey, TValue>"] = "DictionaryFormatter",
        ["System.Collections.Generic.SortedDictionary<TKey, TValue>"] = "SortedDictionaryFormatter",
        ["System.Collections.Generic.SortedList<TKey, TValue>"] = "SortedListFormatter",
        ["System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>"] = "ConcurrentDictionaryFormatter",
        ["System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>"] = "ReadOnlyDictionaryFormatter",
        ["System.Collections.Generic.IDictionary<TKey, TValue>"] = "DictionaryInterfaceFormatter",
        ["System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"] = "ReadOnlyDictionaryInterfaceFormatter",
        ["System.Collections.Immutable.ImmutableDictionary<TKey, TValue>"] = "ImmutableDictionaryFormatter",
        ["System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>"] = "ImmutableSortedDictionaryFormatter",
        ["System.Collections.Immutable.IImmutableDictionary<TKey, TValue>"] = "ImmutableDictionaryInterfaceFormatter",
        ["System.Collections.Generic.PriorityQueue<TElement, TPriority>"] = "PriorityQueueFormatter",
        ["System.Collections.Generic.KeyValuePair<TKey, TValue>"] = "KeyValuePairFormatter",
        ["System.Linq.ILookup<TKey, TElement>"] = "LookupFormatter",
        ["System.Linq.IGrouping<out TKey, out TElement>"] = "GroupingFormatter",
    };

    private static readonly Dictionary<string, string> Simple = new()
    {
        ["string"] = "StringFormatter",
        ["System.Uri"] = "UriFormatter",
        ["System.Version"] = "VersionFormatter",
        ["System.Numerics.BigInteger"] = "BigIntegerFormatter",
        ["System.Text.StringBuilder"] = "StringBuilderFormatter",
        ["System.Type"] = "TypeFormatter",
        ["System.Globalization.CultureInfo"] = "CultureInfoFormatter",
        ["System.TimeZoneInfo"] = "TimeZoneInfoFormatter",
        ["System.Collections.BitArray"] = "BitArrayFormatter",
    };

    /// <summary>
    /// 型に対応する合成フォーマッタの型名を返す。参照型の [ApexPackable] は注釈で null 可否を決める
    /// (注釈なし = null を取らない位置)。対応がなければ実行時解決の CachedFormatter を返す。
    /// </summary>
    public string Formatter(ITypeSymbol type, ISymbol diagnosticTarget)
    {
        if (type is ITypeParameterSymbol)
        {
            return $"{Ns}CachedFormatter<{Name(type)}>";
        }

        if (IsPackable(type))
        {
            bool nonNull = !type.IsValueType && type.NullableAnnotation == NullableAnnotation.NotAnnotated;
            return $"{Ns}{(nonNull ? "PackableNonNullFormatter" : "PackableFormatter")}<{Name(type)}>";
        }

        if (Simple.TryGetValue(type.OriginalDefinition.ToDisplayString(), out var simple))
        {
            return Ns + simple;
        }

        if (ScalarInfo(type) is not null)
        {
            return $"{Ns}ScalarFormatter<{Name(type)}>";
        }

        if (type is IArrayTypeSymbol array)
        {
            var element = array.ElementType;
            if (array.IsSZArray)
            {
                if (IsNullableValue(element, out var inner))
                {
                    return $"{Ns}NullableArrayFormatter<{Name(inner)}, {Formatter(inner, diagnosticTarget)}>";
                }
                return $"{Ns}ArrayFormatter<{Name(element)}, {Formatter(element, diagnosticTarget)}>";
            }
            if (array.Rank >= 2 && array.Rank <= 4)
            {
                return $"{Ns}Array{array.Rank}DFormatter<{Name(element)}, {Formatter(element, diagnosticTarget)}>";
            }
        }

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var args = named.TypeArguments;
            string definition = named.OriginalDefinition.ToDisplayString();

            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return $"{Ns}NullableFormatter<{Name(args[0])}, {Formatter(args[0], diagnosticTarget)}>";
            }
            if (definition == "System.Collections.Generic.List<T>" && IsNullableValue(args[0], out var nullableInner))
            {
                return $"{Ns}NullableListFormatter<{Name(nullableInner)}, {Formatter(nullableInner, diagnosticTarget)}>";
            }
            if (OneArg.TryGetValue(definition, out var one))
            {
                return $"{Ns}{one}<{Name(args[0])}, {Formatter(args[0], diagnosticTarget)}>";
            }
            if (TwoArg.TryGetValue(definition, out var two))
            {
                return $"{Ns}{two}<{Name(args[0])}, {Name(args[1])}, {Formatter(args[0], diagnosticTarget)}, {Formatter(args[1], diagnosticTarget)}>";
            }
            if (named.IsTupleType || definition.StartsWith("System.Tuple<", StringComparison.Ordinal) || definition.StartsWith("System.ValueTuple<", StringComparison.Ordinal))
            {
                var tupleArgs = named.IsTupleType ? named.TupleUnderlyingType?.TypeArguments ?? args : args;
                string kind = named.IsValueType ? "ValueTupleFormatter" : "TupleFormatter";
                var names = tupleArgs.Select(Name);
                var formatters = tupleArgs.Select(a => Formatter(a, diagnosticTarget));
                return $"{Ns}{kind}<{string.Join(", ", names.Concat(formatters))}>";
            }
        }

        if (type.IsUnmanagedType && type.TypeKind == TypeKind.Struct)
        {
            Report(Diagnostics.NonPortableStruct, diagnosticTarget, Name(type));
            return $"{Ns}ScalarFormatter<{Name(type)}>";
        }

        Report(Diagnostics.RuntimeResolved, diagnosticTarget, Name(type));
        return $"{Ns}CachedFormatter<{Name(type)}>";
    }

    private static bool IsNullableValue(ITypeSymbol type, out ITypeSymbol inner)
    {
        if (type is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            inner = n.TypeArguments[0];
            return true;
        }
        inner = type;
        return false;
    }

    // ====================================================================
    // 型の解析
    // ====================================================================

    public TypeModel? Analyze(INamedTypeSymbol symbol, bool report = true)
    {
        if (_cache.TryGetValue(symbol, out var cached)) return cached;
        if (!_inProgress.Add(symbol)) return null; // 構造体の自己参照など

        TypeModel? model;
        int diagnosticsBefore = _diagnostics.Count;
        try
        {
            model = AnalyzeCore(symbol);
        }
        finally
        {
            _inProgress.Remove(symbol);
        }

        if (!report && _diagnostics.Count > diagnosticsBefore)
        {
            // 入れ子として解析しただけの型の診断は、その型自身の解析で出す
            _diagnostics.RemoveRange(diagnosticsBefore, _diagnostics.Count - diagnosticsBefore);
        }
        _cache[symbol] = model;
        return model;
    }

    private TypeModel? AnalyzeCore(INamedTypeSymbol symbol)
    {
        var attribute = GetPackableAttribute(symbol);
        var model = new TypeModel
        {
            Symbol = symbol,
            IsValueType = symbol.IsValueType,
            IsInterface = symbol.TypeKind == TypeKind.Interface,
            IsAbstract = symbol.IsAbstract && symbol.TypeKind != TypeKind.Interface,
            SelfName = SelfName(symbol),
        };

        if (attribute is not null)
        {
            foreach (var arg in attribute.NamedArguments)
            {
                if (arg.Key == "PreserveReferences" && arg.Value.Value is bool preserve) model.PreserveReferences = preserve;
                if (arg.Key == "Versioning" && arg.Value.Value is true) Report(Diagnostics.VersioningNotSupported, symbol, symbol.Name);
                if (arg.Key == "GenerateType" && arg.Value.Value is int generateType && generateType == 1) model.Kind = PackKind.Collection;
            }
        }

        for (var b = symbol.BaseType; b is not null; b = b.BaseType)
        {
            if (!IsPackable(b)) continue;
            // 生成コードは非抽象クラスに ApexMinBodySize、PreserveReferences = true の型に ApexPreserveReferences を出す
            if (!b.IsAbstract) model.HidesBaseMinBodySize = true;
            var baseAttribute = GetPackableAttribute(b);
            if (baseAttribute is not null && baseAttribute.NamedArguments.Any(a => a.Key == "PreserveReferences" && a.Value.Value is true))
            {
                model.HidesBasePreserveReferences = true;
            }
        }

        if (model.IsInterface || model.IsAbstract)
        {
            model.Kind = PackKind.Union;
            AnalyzeUnion(model);
            return model;
        }

        if (model.Kind == PackKind.Collection)
        {
            return AnalyzeCollection(model) ? model : null;
        }

        if (symbol.IsRefLikeType)
        {
            Report(Diagnostics.UnsupportedType, symbol, symbol.Name);
            return null;
        }

        CollectMembers(model);
        if (!ApplyOrder(model)) return null;

        model.Kind = model.IsValueType && !symbol.IsGenericType && model.Members.All(m => GetFixed(m.Type) is not null && !HasAttribute(m.Symbol, "ApexPack.ApexFormatterAttribute"))
            ? PackKind.FixedStruct
            : PackKind.Object;

        if (!SelectConstructor(model)) return null;

        if (model.Kind == PackKind.FixedStruct)
        {
            LayoutFixedStruct(model);
        }
        else
        {
            LayoutObject(model);
        }

        CollectCallbacks(model);
        return model;
    }

    private static string SelfName(INamedTypeSymbol symbol)
        => symbol.TypeParameters.Length == 0
            ? EscapeIdentifier(symbol.Name)
            : $"{EscapeIdentifier(symbol.Name)}<{string.Join(", ", symbol.TypeParameters.Select(t => t.Name))}>";

    internal static string EscapeIdentifier(string name)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None ? "@" + name : name;

    private void AnalyzeUnion(TypeModel model)
    {
        var symbol = model.Symbol;
        foreach (var attr in symbol.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == "ApexPack.ApexUnionAttribute"))
        {
            if (attr.ConstructorArguments.Length != 2 || attr.ConstructorArguments[1].Value is not INamedTypeSymbol derived) continue;
            ushort tag = Convert.ToUInt16(attr.ConstructorArguments[0].Value);
            if (model.UnionCases.Any(c => c.Tag == tag))
            {
                Report(Diagnostics.DuplicateUnionTag, symbol, tag.ToString());
                continue;
            }
            int depth = 0;
            for (var b = derived.BaseType; b is not null; b = b.BaseType) depth++;
            model.UnionCases.Add(new UnionCase { Tag = tag, TypeName = Name(derived), Depth = depth });
        }

        if (model.UnionCases.Count == 0)
        {
            Report(Diagnostics.UnionWithoutCases, symbol, symbol.Name);
        }

        // 派生の深い型から照合する(基底型の case が派生型を先に捕まえないように)
        model.UnionCases = model.UnionCases.OrderByDescending(c => c.Depth).ThenBy(c => c.Tag).ToList();
    }

    private bool AnalyzeCollection(TypeModel model)
    {
        var symbol = model.Symbol;
        var collection = symbol.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>");
        bool hasDefaultCtor = symbol.InstanceConstructors.Any(c => c.Parameters.Length == 0);
        if (collection is null || !hasDefaultCtor || model.IsValueType)
        {
            Report(Diagnostics.InvalidCollectionType, symbol, symbol.Name);
            return false;
        }
        var element = collection.TypeArguments[0];
        model.CollectionElement = Name(element);
        model.CollectionElementFormatter = Formatter(element, symbol);
        return true;
    }

    private void CollectMembers(TypeModel model)
    {
        var symbol = model.Symbol;
        var hierarchy = new List<INamedTypeSymbol>();
        for (var current = symbol; current is not null && current.SpecialType != SpecialType.System_Object && current.SpecialType != SpecialType.System_ValueType; current = current.BaseType)
        {
            hierarchy.Add(current);
        }
        hierarchy.Reverse();

        var members = new List<MemberModel>();
        foreach (var type in hierarchy)
        {
            foreach (var member in type.GetMembers())
            {
                if (member.IsStatic || member.IsImplicitlyDeclared) continue;
                if (HasAttribute(member, "ApexPack.ApexIgnoreAttribute")) continue;

                bool include = member.DeclaredAccessibility == Accessibility.Public || HasAttribute(member, "ApexPack.ApexIncludeAttribute");
                if (!include) continue;
                // 基底型の private メンバーは派生型の生成コードから触れない
                if (!SymbolEqualityComparer.Default.Equals(type, symbol) && member.DeclaredAccessibility == Accessibility.Private) continue;

                MemberModel? m = null;
                if (member is IFieldSymbol field)
                {
                    if (field.IsConst || field.AssociatedSymbol is not null) continue;
                    m = new MemberModel
                    {
                        Symbol = field,
                        Name = field.Name,
                        Type = field.Type,
                        IsReadOnly = field.IsReadOnly,
                        CanSetAfterConstruction = !field.IsReadOnly,
                        IsRequired = field.IsRequired,
                    };
                }
                else if (member is IPropertySymbol property)
                {
                    if (property.IsIndexer || property.GetMethod is null) continue;
                    bool init = property.SetMethod?.IsInitOnly == true;
                    m = new MemberModel
                    {
                        Symbol = property,
                        Name = property.Name,
                        Type = property.Type,
                        IsInitOnly = init,
                        IsReadOnly = property.SetMethod is null,
                        CanSetAfterConstruction = property.SetMethod is not null && !init,
                        IsRequired = property.IsRequired,
                    };
                }

                if (m is null) continue;
                m.TypeName = Name(m.Type);
                members.RemoveAll(existing => existing.Name == m.Name); // 派生側の同名メンバーを優先
                members.Add(m);
            }
        }

        for (int i = 0; i < members.Count; i++) members[i].Order = i;
        model.Members = members;
    }

    private bool ApplyOrder(TypeModel model)
    {
        var withOrder = model.Members
            .Select(m => (Member: m, Attr: GetAttribute(m.Symbol, "ApexPack.ApexOrderAttribute")))
            .ToList();
        int specified = withOrder.Count(x => x.Attr is not null);
        if (specified == 0) return true;
        if (specified != withOrder.Count)
        {
            Report(Diagnostics.InvalidOrder, model.Symbol, model.Symbol.Name);
            return false;
        }
        var ordered = withOrder.Select(x => (x.Member, Order: (int)x.Attr!.ConstructorArguments[0].Value!)).ToList();
        if (ordered.Select(x => x.Order).Distinct().Count() != ordered.Count)
        {
            Report(Diagnostics.InvalidOrder, model.Symbol, model.Symbol.Name);
            return false;
        }
        model.Members = ordered.OrderBy(x => x.Order).Select(x => x.Member).ToList();
        for (int i = 0; i < model.Members.Count; i++) model.Members[i].Order = i;
        return true;
    }

    private bool SelectConstructor(TypeModel model)
    {
        var symbol = model.Symbol;
        var ctors = symbol.InstanceConstructors
            .Where(c => !IsCopyConstructor(c, symbol))
            .ToList();

        IMethodSymbol? chosen = ctors.FirstOrDefault(c => HasAttribute(c, "ApexPack.ApexConstructorAttribute"));
        bool allAssignable = model.Members.All(m => m.CanSetAfterConstruction || m.IsInitOnly);

        if (chosen is null)
        {
            var parameterless = ctors.FirstOrDefault(c => c.Parameters.Length == 0);
            if (parameterless is not null && allAssignable)
            {
                chosen = parameterless;
            }
            else
            {
                var withParameters = ctors.Where(c => c.Parameters.Length > 0).ToList();
                if (withParameters.Count == 1)
                {
                    chosen = withParameters[0];
                }
                else if (parameterless is not null)
                {
                    chosen = parameterless; // 代入できないメンバーは後で除外される
                }
                else if (model.IsValueType && allAssignable)
                {
                    chosen = null; // 構造体の既定値から作る
                }
                else
                {
                    Report(Diagnostics.NoConstructor, symbol, symbol.Name);
                    return false;
                }
            }
        }

        model.Constructor = chosen is { Parameters.Length: > 0 } ? chosen : null;
        if (model.Constructor is not null)
        {
            foreach (var parameter in model.Constructor.Parameters)
            {
                var member = model.Members.FirstOrDefault(m => string.Equals(m.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                if (member is null)
                {
                    Report(Diagnostics.ConstructorParameterMismatch, model.Constructor, parameter.Name);
                    return false;
                }
                member.CtorParameterIndex = parameter.Ordinal;
                model.ConstructorArguments.Add(member);
            }
        }

        // コンストラクタでも代入でも設定できないメンバーは直列化しない
        model.Members = model.Members.Where(m => m.CtorParameterIndex >= 0 || m.CanSetAfterConstruction || m.IsInitOnly).ToList();
        for (int i = 0; i < model.Members.Count; i++) model.Members[i].Order = i;

        model.CreateEarly = !model.IsValueType
            && model.Constructor is null
            && model.Members.All(m => m.CanSetAfterConstruction && !m.IsRequired);
        return true;
    }

    private static bool IsCopyConstructor(IMethodSymbol ctor, INamedTypeSymbol type)
        => type.IsRecord && ctor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, type);

    private void LayoutFixedStruct(TypeModel model)
    {
        int offset = 0;
        bool raw = model.Symbol.DeclaringSyntaxReferences.Length == 1
            && !HasAttribute(model.Symbol, "System.Runtime.InteropServices.StructLayoutAttribute")
            && FieldsMatchMembers(model);
        foreach (var m in model.Members)
        {
            m.Encoding = MemberEncoding.Fixed;
            m.Fixed = GetFixed(m.Type)!;
            offset += m.Fixed.Size;
            if (!m.Fixed.IsRaw) raw = false;
            if (m.Fixed.IsPackable) model.FixedRawConditions.Add($"{m.Fixed.TypeName}.ApexIsRawLayout");
        }
        model.FixedBytes = offset;
        model.FixedRawEligible = raw;
    }

    /// <summary>構造体のインスタンスフィールドが、直列化メンバーと同じ順序で 1 対 1 に対応するか。</summary>
    private static bool FieldsMatchMembers(TypeModel model)
    {
        var fields = model.Symbol.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst).ToList();
        if (fields.Count != model.Members.Count) return false;
        for (int i = 0; i < fields.Count; i++)
        {
            var target = (ISymbol?)fields[i].AssociatedSymbol ?? fields[i];
            if (!SymbolEqualityComparer.Default.Equals(target, model.Members[i].Symbol)) return false;
        }
        return true;
    }

    private void LayoutObject(TypeModel model)
    {
        int bit = 0;
        int fixedBytes = 0;
        int nullableFixed = 0;

        foreach (var m in model.Members)
        {
            var custom = GetAttribute(m.Symbol, "ApexPack.ApexFormatterAttribute");
            if (custom is not null && custom.ConstructorArguments.Length == 1 && custom.ConstructorArguments[0].Value is ITypeSymbol formatterType)
            {
                m.Encoding = MemberEncoding.Variable;
                m.Formatter = Name(formatterType);
                continue;
            }

            var type = m.Type;
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                m.Encoding = MemberEncoding.BoolBit;
                continue;
            }

            if (IsNullableValue(type, out var inner))
            {
                var innerFixed = GetFixed(inner);
                if (innerFixed is not null)
                {
                    m.Encoding = MemberEncoding.NullableFixed;
                    m.Fixed = innerFixed;
                    nullableFixed += innerFixed.Size;
                }
                else
                {
                    m.Encoding = MemberEncoding.NullableVariable;
                    m.Formatter = Formatter(inner, m.Symbol);
                }
                continue;
            }

            var fixedInfo = GetFixed(type);
            if (fixedInfo is not null)
            {
                m.Encoding = MemberEncoding.Fixed;
                m.Fixed = fixedInfo;
                fixedBytes += fixedInfo.Size;
                continue;
            }

            // null を取り得る参照型(? 注釈、または nullable 無効の文脈)は、null をマスクの 1 ビットで表し本体は値があるときだけ書く
            bool nullableReference = !type.IsValueType && type is not ITypeParameterSymbol && type.NullableAnnotation != NullableAnnotation.NotAnnotated;

            if (TryInlineCollection(type, out var element, out bool isList))
            {
                m.Encoding = MemberEncoding.InlineCollection;
                m.InlineIsList = isList;
                m.InlineElement = Name(element);
                m.Formatter = Formatter(element, m.Symbol);
                m.BitGuarded = nullableReference;
                continue;
            }

            m.Encoding = MemberEncoding.Variable;
            m.Formatter = !type.IsValueType && type is not ITypeParameterSymbol && IsPackable(type)
                ? $"{Ns}PackableNonNullFormatter<{Name(type)}>"
                : Formatter(type, m.Symbol);
            m.BitGuarded = nullableReference;
        }

        // ビットはマスクの宣言順に割り当てる(クラスでマスクがあれば bit0 は非 null の印)
        int bitCount = model.Members.Count(UsesBit);
        model.BitCount = bitCount;
        model.HasMarkerBit = !model.IsValueType && bitCount > 0;
        bit = model.HasMarkerBit ? 1 : 0;
        foreach (var m in model.Members)
        {
            if (UsesBit(m))
            {
                m.Bit = bit++;
            }
        }

        model.FixedBytes = fixedBytes;
        model.NullableFixedMaxBytes = nullableFixed;
    }

    private static bool UsesBit(MemberModel m)
        => m.BitGuarded || m.Encoding is MemberEncoding.BoolBit or MemberEncoding.NullableFixed or MemberEncoding.NullableVariable;

    private bool TryInlineCollection(ITypeSymbol type, out ITypeSymbol element, out bool isList)
    {
        element = type;
        isList = false;
        if (type is IArrayTypeSymbol { IsSZArray: true } array)
        {
            element = array.ElementType;
        }
        else if (type is INamedTypeSymbol named && named.IsGenericType && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
        {
            element = named.TypeArguments[0];
            isList = true;
        }
        else
        {
            return false;
        }
        return !element.IsValueType && element is not ITypeParameterSymbol && IsPackable(element);
    }

    private void CollectCallbacks(TypeModel model)
    {
        foreach (var method in model.Symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary || method.Parameters.Length != 0) continue;
            var callback = new CallbackModel { Name = EscapeIdentifier(method.Name), IsStatic = method.IsStatic };
            if (HasAttribute(method, "ApexPack.ApexOnSerializingAttribute")) model.OnSerializing.Add(callback);
            if (HasAttribute(method, "ApexPack.ApexOnSerializedAttribute")) model.OnSerialized.Add(callback);
            if (HasAttribute(method, "ApexPack.ApexOnDeserializedAttribute")) model.OnDeserialized.Add(callback);
            if (HasAttribute(method, "ApexPack.ApexOnDeserializingAttribute"))
            {
                if (!method.IsStatic && !model.CreateEarly)
                {
                    Report(Diagnostics.InstanceOnDeserializing, method, method.Name);
                    continue;
                }
                model.OnDeserializing.Add(callback);
            }
        }
    }

    private void Report(DiagnosticDescriptor descriptor, ISymbol symbol, params object[] args)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
        _diagnostics.Add(Diagnostic.Create(descriptor, location, args));
    }
}
