using ApexPack.BenchmarkSuite.Models;
using ApexPack.BenchmarkSuite.Verification;

namespace ApexPack.BenchmarkSuite.Runner;

internal static class ComparisonScenarios
{
    private const string ShortText = "item";
    private static readonly string s_longText = new('a', 256);

    internal static void AddTo(List<IScenario> scenarios)
    {
        AddShape(scenarios, "Struct16", "固定長 struct 16 B", static i => FixedValue(i));
        AddShape(scenarios, "Struct128", "固定長 struct 128 B", static i => new BenchFixedValue128
        {
            A = FixedValue(i), B = FixedValue(i + 4), C = FixedValue(i + 8), D = FixedValue(i + 12),
            E = FixedValue(i + 16), F = FixedValue(i + 20), G = FixedValue(i + 24), H = FixedValue(i + 28)
        });
        AddShape(scenarios, "Class16", "固定長 class 16 B", static i => FixedClass(i));
        AddShape(scenarios, "Class128", "固定長 class 128 B", static i => new BenchFixedClass128
        {
            A = FixedValue(i), B = FixedValue(i + 4), C = FixedValue(i + 8), D = FixedValue(i + 12),
            E = FixedValue(i + 16), F = FixedValue(i + 20), G = FixedValue(i + 24), H = FixedValue(i + 28)
        });
        AddShape(scenarios, "StructShortText", "struct + 短い文字列", static i => new BenchTextValue { Id = i, Text = ShortText });
        AddShape(scenarios, "StructLongText", "struct + 長い文字列", static i => new BenchTextValue { Id = i, Text = s_longText });
        AddShape(scenarios, "ClassShortText", "class + 短い文字列", static i => new BenchTextClass { Id = i, Text = ShortText });
        AddShape(scenarios, "ClassLongText", "class + 長い文字列", static i => new BenchTextClass { Id = i, Text = s_longText });

        foreach (int count in new[] { 32, 1024 })
        {
            AddSequences(scenarios, "Int32", "int", count, static i => i);
            AddSequences(scenarios, "Class16", "固定長 class 16 B", count, static i => FixedClass(i));
            AddMaps(scenarios, "Int32", "int", count, static i => i, static (left, right) => left == right);
            AddMaps(scenarios, "Class16", "固定長 class 16 B", count, static i => FixedClass(i),
                static (left, right) => DeepGraphVerifier.AreEqual(left, right));
        }

        foreach (int count in new[] { 100, 1000 })
        {
            AddGraph(scenarios, "Chain", "チェーン", count, GraphShape.Chain);
            AddGraph(scenarios, "Tree", "二分木", count, GraphShape.Tree);
            AddGraph(scenarios, "Shared", "共有・非循環", count, GraphShape.Shared);
            AddGraph(scenarios, "Cycle", "循環", count, GraphShape.Cycle);
        }

        Add(scenarios, "Nullability", "RequiredText", "string（全値非null）",
            static () => Fill(1024, static i => new BenchTextClass { Id = i, Text = ShortText }));
        Add(scenarios, "Nullability", "OptionalText", "string?（全値非null）",
            static () => Fill(1024, static i => new BenchOptionalText { Id = i, Text = ShortText }));
        Add(scenarios, "Nullability", "OptionalText33", "string?（約33% null）",
            static () => Fill(1024, static i => new BenchOptionalText { Id = i, Text = i % 3 == 0 ? null : ShortText }));
        Add(scenarios, "Nullability", "OptionalText100", "string?（全値null）",
            static () => Fill(1024, static i => new BenchOptionalText { Id = i }));
        Add(scenarios, "Nullability", "RequiredChild", "固定長の子 class（全値非null）",
            static () => Fill(1024, static i => new BenchRequiredChild { Child = FixedClass(i) }));
        Add(scenarios, "Nullability", "OptionalChild", "固定長の子 class?（全値非null）",
            static () => Fill(1024, static i => new BenchOptionalChild { Child = FixedClass(i) }));
    }

    private static BenchFixedValue16 FixedValue(int i) => new() { A = i, B = i + 1, C = i + 2, D = i + 3 };
    private static BenchFixedClass16 FixedClass(int i) => new() { A = i, B = i + 1, C = i + 2, D = i + 3 };

    private static void AddShape<T>(List<IScenario> scenarios, string id, string label, Func<int, T> factory)
    {
        Add(scenarios, "Shape", id + "_Single", $"{label}・単体", () => factory(1));
        foreach (int count in new[] { 1, 32, 1024 })
            Add(scenarios, "Shape", $"{id}_Array{count}", $"{label}・配列 {count:N0} 件", () => Fill(count, factory));
    }

    private static void AddSequences<T>(List<IScenario> scenarios, string id, string label, int count, Func<int, T> factory)
    {
        Add(scenarios, "Collection", $"Array_{id}_{count}", $"配列<{label}> {count:N0} 件", () => Fill(count, factory));
        Add(scenarios, "Collection", $"List_{id}_{count}", $"List<{label}> {count:N0} 件", () => new List<T>(Fill(count, factory)));
        Add(scenarios, "Collection", $"LinkedList_{id}_{count}", $"LinkedList<{label}> {count:N0} 件", () => new LinkedList<T>(Fill(count, factory)));
    }

    private static void AddMaps<T>(List<IScenario> scenarios, string id, string label, int count, Func<int, T> factory, Func<T, T, bool> valuesEqual)
    {
        Dictionary<int, T> CreatePairs() => Enumerable.Range(0, count).ToDictionary(static i => i * 17, factory);
        bool PairsEqual(IEnumerable<KeyValuePair<int, T>> a, IEnumerable<KeyValuePair<int, T>> b)
        {
            using var actual = b.GetEnumerator();
            foreach (var pair in a)
                if (!actual.MoveNext() || actual.Current.Key != pair.Key || !valuesEqual(pair.Value, actual.Current.Value))
                    return false;
            return !actual.MoveNext();
        }

        Add(scenarios, "Collection", $"Dictionary_{id}_{count}", $"Dictionary<int, {label}> {count:N0} 件",
            CreatePairs, (a, b) => PairsEqual(a.OrderBy(static pair => pair.Key), b.OrderBy(static pair => pair.Key)));
        Add(scenarios, "Collection", $"SortedDictionary_{id}_{count}", $"SortedDictionary<int, {label}> {count:N0} 件",
            () => new SortedDictionary<int, T>(CreatePairs()), (a, b) => PairsEqual(a, b));
        Add(scenarios, "Collection", $"SortedList_{id}_{count}", $"SortedList<int, {label}> {count:N0} 件",
            () => new SortedList<int, T>(CreatePairs()), (a, b) => PairsEqual(a, b));
    }

    private static T[] Fill<T>(int count, Func<int, T> factory)
    {
        var values = new T[count];
        for (int i = 0; i < values.Length; i++) values[i] = factory(i + 1);
        return values;
    }

    private static void Add<T>(List<IScenario> scenarios, string category, string id, string label, Func<T> factory, Func<T, T, bool>? verify = null)
        => scenarios.Add(new Scenario<T, PublicOps<T>>($"Compare_{category}_{id}", category, label, factory, verify));

    private enum GraphShape { Chain, Tree, Shared, Cycle }

    private static void AddGraph(List<IScenario> scenarios, string id, string label, int count, GraphShape shape)
        => scenarios.Add(new Scenario<BenchGraphNode, GraphOps>($"Compare_Graph_{id}_{count}", "Graph",
            $"参照グラフ・{label} {count:N0} ノード", () => CreateGraph(count, shape), PreservesGraph));

    private static BenchGraphNode CreateGraph(int count, GraphShape shape)
    {
        var nodes = Fill(count, static i => new BenchGraphNode { Id = i, Name = ShortText });
        for (int i = 0; i < count; i++)
        {
            if (shape == GraphShape.Tree)
            {
                int child = 2 * i + 1;
                if (child < count) nodes[i].Next = nodes[child];
                if (child + 1 < count) nodes[i].Other = nodes[child + 1];
                continue;
            }
            if (i + 1 < count) nodes[i].Next = nodes[i + 1];
            if (shape == GraphShape.Shared && i + 2 < count) nodes[i].Other = nodes[i + 2];
        }
        if (shape == GraphShape.Cycle) nodes[^1].Next = nodes[0];
        return nodes[0];
    }

    internal static bool PreservesGraph(BenchGraphNode expected, BenchGraphNode actual)
    {
        var seen = new Dictionary<BenchGraphNode, BenchGraphNode>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(BenchGraphNode? Expected, BenchGraphNode? Actual)>();
        pending.Push((expected, actual));
        while (pending.TryPop(out var pair))
        {
            var (left, right) = pair;
            if (left is null || right is null)
            {
                if (!ReferenceEquals(left, right)) return false;
                continue;
            }
            if (seen.TryGetValue(left, out var mapped))
            {
                if (!ReferenceEquals(mapped, right)) return false;
                continue;
            }
            if (left.Id != right.Id || left.Name != right.Name) return false;
            seen.Add(left, right);
            pending.Push((left.Next, right.Next));
            pending.Push((left.Other, right.Other));
        }
        return true;
    }
}
