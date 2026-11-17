using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ApexPack.Generator;

/// <summary>
/// [ApexPackable] 型に ISerializePackable&lt;T&gt; の実装を生成する。
/// </summary>
[Generator]
public sealed class ApexPackGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider.ForAttributeWithMetadataName(
            "ApexPack.ApexPackableAttribute",
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        // MemoryPack 互換層(ApexPack.MemoryPackCompat)の属性。本物の MemoryPack の属性は TypeAnalyzer が除外する
        var compatTypes = context.SyntaxProvider.ForAttributeWithMetadataName(
            "MemoryPack.MemoryPackableAttribute",
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        var externalUnions = context.SyntaxProvider.ForAttributeWithMetadataName(
            "MemoryPack.MemoryPackUnionFormatterAttribute",
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        var invalidUnionTargets = context.SyntaxProvider.ForAttributeWithMetadataName(
            "ApexPack.ApexUnionAttribute",
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, cancellationToken) =>
            {
                var type = (INamedTypeSymbol)ctx.TargetSymbol;
                // A compatibility formatter holder supplies cases for a different type.
                if (type.TypeKind != TypeKind.Class || (type.IsAbstract && !type.IsStatic)
                    || TypeAnalyzer.AnalyzeExternalUnions(type).Any())
                {
                    return null;
                }

                var location = ctx.Attributes[0].ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                    ?? ctx.TargetNode.GetLocation();
                return Diagnostic.Create(Diagnostics.InvalidUnionTarget, location, type.Name);
            });
        context.RegisterSourceOutput(invalidUnionTargets, static (ctx, diagnostic) =>
        {
            if (diagnostic is not null) ctx.ReportDiagnostic(diagnostic);
        });

        var all = types.Collect().Combine(compatTypes.Collect()).Combine(externalUnions.Collect());
        var source = context.CompilationProvider.Combine(all);
        context.RegisterSourceOutput(source, static (spc, input) =>
            Execute(spc, input.Left, input.Right.Left.Left.AddRange(input.Right.Left.Right), input.Right.Right));
    }

    private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<INamedTypeSymbol> symbols, ImmutableArray<INamedTypeSymbol> unionHolders)
    {
        if (symbols.IsDefaultOrEmpty && unionHolders.IsDefaultOrEmpty) return;

        var diagnostics = new List<Diagnostic>();
        var analyzer = new TypeAnalyzer(compilation, diagnostics);
        var registrations = new List<TypeModel>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var symbol in symbols)
        {
            if (!seen.Add(symbol)) continue;
            if (TypeAnalyzer.GetPackableAttribute(symbol) is null) continue; // 本物の MemoryPack の属性
            if (!CheckPartial(symbol, diagnostics)) continue;

            var model = analyzer.Analyze(symbol);
            if (model is null) continue;
            model.EmitRootFormatter = !symbol.IsValueType && CanRegister(symbol);

            context.AddSource(HintName(symbol), SourceText.From(CodeEmitter.Emit(model), Encoding.UTF8));
            if (CanRegister(symbol)) registrations.Add(model);
        }

        foreach (var diagnostic in diagnostics) context.ReportDiagnostic(diagnostic);

        var external = new List<ExternalUnionModel>();
        foreach (var holder in unionHolders.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            external.AddRange(TypeAnalyzer.AnalyzeExternalUnions(holder));
        }

        if (registrations.Count > 0 || external.Count > 0)
        {
            context.AddSource("ApexPack.ModuleInitializer.g.cs", SourceText.From(EmitRegistration(compilation, registrations, external), Encoding.UTF8));
        }
    }

    private static bool CheckPartial(INamedTypeSymbol symbol, List<Diagnostic> diagnostics)
    {
        if (!IsPartial(symbol))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.MustBePartial, symbol.Locations.FirstOrDefault(), symbol.Name));
            return false;
        }
        for (var c = symbol.ContainingType; c is not null; c = c.ContainingType)
        {
            if (!IsPartial(c))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.ContainerMustBePartial, symbol.Locations.FirstOrDefault(), c.Name, symbol.Name));
                return false;
            }
        }
        return true;
    }

    private static bool IsPartial(INamedTypeSymbol symbol)
        => symbol.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static string HintName(INamedTypeSymbol symbol)
    {
        var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        if (symbol.Arity > 0) sb.Append('`').Append(symbol.Arity);
        return sb.Append(".ApexPack.g.cs").ToString();
    }

    /// <summary>モジュール初期化子から参照できる(非ジェネリックで private/protected でない)型だけ登録する。</summary>
    private static bool CanRegister(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? t = symbol; t is not null; t = t.ContainingType)
        {
            if (t.IsGenericType) return false;
            if (t.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)) return false;
        }
        return true;
    }

    /// <summary>
    /// 型ごとのフォーマッタをジェネリック static スロットへ登録する(NativeAOT でもリフレクションなしで解決するため)。
    /// 登録先は型ごとの <c>ApexFormatterCache&lt;T&gt;</c> だけで、この型がアンロードされれば一緒に解放される。
    /// </summary>
    private static string EmitRegistration(Compilation compilation, List<TypeModel> models, List<ExternalUnionModel> externalUnions)
    {
        var sb = new StringBuilder();
        sb.Append("// <auto-generated> ApexPack.Generator </auto-generated>\n");
        sb.Append("#pragma warning disable CA2255, CS0612, CS0618\n");
        var asm = compilation.AssemblyName ?? "Assembly";
        var id = new StringBuilder();
        foreach (char c in asm) id.Append(char.IsLetterOrDigit(c) ? c : '_');
        sb.Append("namespace ApexPack.Generated\n{\n");
        sb.Append($"    internal static class ApexPackModuleInitializer_{id}\n    {{\n");
        sb.Append("        [global::System.Runtime.CompilerServices.ModuleInitializer]\n");
        sb.Append("        internal static void Initialize()\n        {\n");
        foreach (var model in models)
        {
            var name = TypeAnalyzer.Name(model.Symbol);
            string preserve = model.RootPreserveReferences ? "true" : "false";
            sb.Append(model.EmitRootFormatter
                ? $"            global::ApexPack.ApexFormatterCache<{name}>.Register(new {name}.__ApexRootFormatter({preserve}));\n"
                : $"            global::ApexPack.ApexFormatterCache<{name}>.Register(new global::ApexPack.FormatterAdapter<{name}, global::ApexPack.PackableFormatter<{name}>>({preserve}));\n");
        }
        // MemoryPack 互換の [MemoryPackUnionFormatter]: 実行時 Union として登録する(開いたジェネリックは閉じた型ごとに作る)
        foreach (var union in externalUnions)
        {
            var cases = string.Join(", ", union.Cases.Select(c => $"((ushort){c.Tag}, typeof({c.TypeName}))"));
            if (union.IsOpenGeneric)
            {
                sb.Append($"            global::ApexPack.ApexDynamicUnion.RegisterGeneric(typeof({union.OpenUnionTypeName}), {cases});\n");
            }
            else
            {
                sb.Append($"            global::ApexPack.ApexFormatterCache<{union.UnionTypeName}>.Register(new global::ApexPack.ApexDynamicUnionFormatter<{union.UnionTypeName}>({cases}));\n");
            }
        }
        sb.Append("        }\n    }\n}\n");
        return sb.ToString();
    }
}

internal static class Diagnostics
{
    private const string Category = "ApexPack";

    public static readonly DiagnosticDescriptor MustBePartial = new(
        "APX001", "Type must be partial", "[ApexPackable] type '{0}' must be declared partial", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ContainerMustBePartial = new(
        "APX002", "Containing type must be partial", "Containing type '{0}' of [ApexPackable] type '{1}' must be declared partial", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor RuntimeResolved = new(
        "APX003", "Formatter resolved at runtime",
        "No compile-time formatter for '{0}'; it is resolved through ApexFormatterCache at runtime (register a formatter or mark the type [ApexPackable])",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor NonPortableStruct = new(
        "APX004", "Struct is serialized by raw memory copy",
        "'{0}' is not [ApexPackable]; it is copied as raw memory, which is not portable across endianness or layout changes",
        Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor NoConstructor = new(
        "APX005", "No usable constructor",
        "[ApexPackable] type '{0}' needs a parameterless constructor, a single constructor, or one marked [ApexConstructor]",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ConstructorParameterMismatch = new(
        "APX006", "Constructor parameter does not match a member",
        "Constructor parameter '{0}' does not match any serialized member (names are compared case-insensitively)",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnionWithoutCases = new(
        "APX007", "Abstract type without [ApexUnion]",
        "Abstract class or interface '{0}' needs at least one [ApexUnion(tag, typeof(Derived))]", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor VersioningNotSupported = new(
        "APX008", "Versioning is not supported", "[ApexPackable(Versioning = true)] on '{0}' is not supported", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InstanceOnDeserializing = new(
        "APX009", "Instance [ApexOnDeserializing] needs early construction",
        "Instance method '{0}' marked [ApexOnDeserializing] requires a class with a parameterless constructor and only settable members; make it static",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidOrder = new(
        "APX010", "Invalid [ApexOrder]", "When [ApexOrder] is used on '{0}', every serialized member needs a unique order", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor DuplicateUnionTag = new(
        "APX011", "Duplicate union tag", "Union tag {0} is registered more than once", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidCollectionType = new(
        "APX012", "Invalid collection type",
        "GenerateType.Collection type '{0}' must be a class implementing ICollection<T> with a parameterless constructor", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "APX013", "Unsupported type", "'{0}' cannot be [ApexPackable] (ref struct)", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidUnionTarget = new(
        "APX014", "Invalid [ApexUnion] target",
        "[ApexUnion] on class '{0}' requires an abstract class; declare Union cases on an abstract class or interface",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ConflictingMemberAttributes = new(
        "APX015", "Conflicting member attributes",
        "Member '{0}' cannot have both [ApexIgnore] and [ApexInclude]", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MultipleApexConstructors = new(
        "APX016", "Multiple [ApexConstructor] attributes",
        "Type '{0}' can mark only one constructor with [ApexConstructor]", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor CallbackHasParameters = new(
        "APX017", "ApexPack callback has parameters",
        "ApexPack callback method '{0}' must be parameterless", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnionCollection = new(
        "APX018", "Union cannot use collection generation",
        "Union type '{0}' cannot use GenerateType.Collection", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidUnionCase = new(
        "APX019", "Invalid [ApexUnion] case",
        "Union case '{0}' must inherit or implement '{1}'", Category, DiagnosticSeverity.Error, true);
}
