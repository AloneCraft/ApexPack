using ApexPack.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ApexPack.Tests;

public class GeneratorAttributeDiagnosticTests
{
    [Theory]
    [InlineData("APX015", "[ApexPackable] partial class C { [ApexIgnore, ApexInclude] public int X; }", "ApexInclude")]
    [InlineData("APX015", "[ApexPackable] partial class C { [ApexInclude, ApexIgnore] private int X { get; set; } }", "ApexInclude")]
    [InlineData("APX016", "[ApexPackable] partial class C { [ApexConstructor] public C() {} [ApexConstructor] public C(int x) {} }", "ApexConstructor")]
    [InlineData("APX017", "[ApexPackable] partial class C { [ApexOnSerializing] void M(int x) {} }", "ApexOnSerializing")]
    [InlineData("APX017", "[ApexPackable] partial class C { [ApexOnSerialized] static void M(int x) {} }", "ApexOnSerialized")]
    [InlineData("APX017", "[ApexPackable] partial class C { [ApexOnDeserializing] static void M(int x) {} }", "ApexOnDeserializing")]
    [InlineData("APX017", "[ApexPackable] partial class C { [ApexOnDeserialized] void M(int x) {} }", "ApexOnDeserialized")]
    [InlineData("APX018", "[ApexPackable(GenerateType = ApexGenerateType.Collection), ApexUnion(0, typeof(Child))] partial interface C {} sealed class Child : C {}",
        "GenerateType = ApexGenerateType.Collection")]
    [InlineData("APX018", "[ApexPackable(GenerateType = ApexGenerateType.Collection), ApexUnion(0, typeof(Child))] abstract partial class C {} sealed class Child : C {}",
        "GenerateType = ApexGenerateType.Collection")]
    [InlineData("APX019", "[ApexPackable, ApexUnion(0, typeof(string))] abstract partial class C {}", "typeof(string)")]
    [InlineData("APX019", "[ApexPackable, ApexUnion(derivedType: typeof(Outsider), tag: 0)] partial interface C {} sealed class Outsider {}",
        "typeof(Outsider)")]
    [InlineData("APX019", "[ApexPackable, ApexUnion(0, typeof(Outsider<>))] partial interface C<T> {} sealed class Outsider<T> {}",
        "typeof(Outsider<>)")]
    public void InvalidAttribute_ReportsErrorAtUserDeclaration(string expectedId, string declaration, string expectedSite)
    {
        string source = "using ApexPack;\n" + declaration;
        var tree = CSharpSyntaxTree.ParseText(source, path: "UserModel.cs");
        string[] platformPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = platformPaths.Where(path => Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll")
            .Append(typeof(ApexPackableAttribute).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ApexPackDiagnosticFixture", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ApexPackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);

        var error = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal(expectedId, error.Id);
        Assert.Same(tree, error.Location.SourceTree);
        var expectedSpan = new TextSpan(source.LastIndexOf(expectedSite, StringComparison.Ordinal), expectedSite.Length);
        Assert.Equal(expectedSpan, error.Location.SourceSpan);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
        Assert.DoesNotContain(updated.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
