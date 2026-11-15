using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using ApexPack;
using ApexPack.Parallel;

namespace ApexPack.Tests.Plugin;

[ApexPackable]
public partial class PluginChild
{
    public int Value { get; set; }
}

[ApexPackable]
[ApexUnion(0, typeof(PluginCircle))]
public abstract partial class PluginShape
{
    public string? Label { get; set; }
}

[ApexPackable]
public partial class PluginCircle : PluginShape
{
    public double Radius { get; set; }
}

[ApexPackable]
public partial class PluginItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<PluginChild> Children { get; set; } = new();
    public Dictionary<string, PluginChild>? Map { get; set; }
    public PluginShape? Shape { get; set; }
    public Type? Kind { get; set; }
    public PluginChild?[]? Optional { get; set; }
}

[ApexPackable(PreserveReferences = true)]
public partial class PluginNode
{
    public int Id { get; set; }
    public PluginNode? Next { get; set; }
}

[ApexPackable]
public partial class PluginGeneric<T>
{
    public T? Value { get; set; }
}

/// <summary>参照保持の型を含むだけの型(ルートで参照追跡が始まる経路)。</summary>
[ApexPackable]
public partial class PluginNodeHolder
{
    public List<PluginNode>? Nodes { get; set; }
}

/// <summary>実行時 Union の基底(属性なし)。</summary>
public interface IPluginDynamic
{
}

[ApexPackable]
public partial class PluginDynamicCase : IPluginDynamic
{
    public int Value { get; set; }
}

/// <summary>ジェネリック型の一括登録で扱う型。</summary>
public class PluginBox<T>
{
    public T? Value { get; set; }
}

public sealed class PluginBoxFormatter<T> : ApexFormatter<PluginBox<T>>
{
    public override void Write<TWriter, TTracker>(ref TWriter writer, scoped in PluginBox<T> value, ref TTracker tracker)
    {
        var inner = value.Value!;
        ApexFormatterCache<T>.Instance.Write(ref writer, in inner, ref tracker);
    }

    public override PluginBox<T> Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        => new() { Value = ApexFormatterCache<T>.Instance.Read(ref reader, ref tracker, ref guard) };
}

/// <summary>ApexPack の各経路を一通り通す入口。テストからリフレクションで呼ぶ。</summary>
public static class PluginEntry
{
    public static string Run()
    {
        // Type 値の名前解決をこのアセンブリの文脈で行う
        using var scope = AssemblyLoadContext.EnterContextualReflection(typeof(PluginEntry).Assembly);

        var item = new PluginItem
        {
            Id = 1,
            Name = "plugin",
            Children = { new PluginChild { Value = 2 } },
            Map = new Dictionary<string, PluginChild> { ["k"] = new PluginChild { Value = 3 } },
            Shape = new PluginCircle { Label = "c", Radius = 4 },
            Kind = typeof(PluginChild),
            Optional = new PluginChild?[] { null, new PluginChild { Value = 5 } },
        };

        // 制約付き(生成コード直結)
        var a = ApexPackSerializer.Deserialize<PluginItem>(ApexPackSerializer.Serialize(item));

        // 制約なし(ApexFormatterCache<T> と実行時解決)
        var list = new List<PluginItem> { item, item };
        var b = ApexPackSerializer.Deserialize<List<PluginItem>>(ApexPackSerializer.Serialize(list));
        _ = ApexPackSerializer.Deserialize<Dictionary<int, PluginShape?>>(ApexPackSerializer.Serialize(new Dictionary<int, PluginShape?> { [1] = item.Shape, [2] = null }));
        _ = ApexPackSerializer.Deserialize<PluginGeneric<PluginChild>>(ApexPackSerializer.Serialize(new PluginGeneric<PluginChild> { Value = new PluginChild { Value = 6 } }));

        // 参照追跡(属性による自動有効化)
        var n1 = new PluginNode { Id = 1 };
        n1.Next = new PluginNode { Id = 2, Next = n1 };
        var c = ApexPackSerializer.Deserialize<PluginNode>(ApexPackSerializer.Serialize(n1));

        // 並列
        var many = new PluginItem[100];
        for (int i = 0; i < many.Length; i++) many[i] = item;
        var d = ApexPackParallel.Deserialize<PluginItem>(ApexPackParallel.Serialize(many, chunkSize: 16), chunkSize: 16);

        // 信頼できない入力(上限付き)+ 明示的な型解決
        var e = ApexPackSerializer.Deserialize<PluginItem>(ApexPackSerializer.Serialize(item), new ApexSerializerOptions
        {
            Security = ApexSecurityOptions.Default,
            TypeResolver = name => typeof(PluginEntry).Assembly.GetType(name.Split(',')[0]),
        });

        // ルートからの参照追跡(参照保持の型を含むだけの型)
        var holder = new PluginNodeHolder { Nodes = new List<PluginNode> { n1, n1 } };
        var f = ApexPackSerializer.Deserialize<PluginNodeHolder>(ApexPackSerializer.Serialize(holder));

        // 実行時 Union と、ジェネリック型の一括登録(登録表は型を強く握らない)
        ApexFormatterCache<IPluginDynamic>.Register(new ApexDynamicUnionFormatter<IPluginDynamic>((0, typeof(PluginDynamicCase))));
        var g = ApexPackSerializer.Deserialize<IPluginDynamic>(ApexPackSerializer.Serialize<IPluginDynamic>(new PluginDynamicCase { Value = 7 }));
        ApexFormatterRegistry.RegisterGeneric(typeof(PluginBox<>), typeof(PluginBoxFormatter<>));
        var h = ApexPackSerializer.Deserialize<PluginBox<PluginChild>>(ApexPackSerializer.Serialize(new PluginBox<PluginChild> { Value = new PluginChild { Value = 8 } }));

        bool ok = a.Children[0].Value == 2 && a.Map!["k"].Value == 3 && ((PluginCircle)a.Shape!).Radius == 4
            && a.Kind == typeof(PluginChild) && a.Optional![1]!.Value == 5
            && b.Count == 2 && ReferenceEquals(c, c.Next!.Next) && d.Length == 100 && e.Kind == typeof(PluginChild)
            && ReferenceEquals(f.Nodes![0], f.Nodes[1]) && ((PluginDynamicCase)g).Value == 7 && h.Value!.Value == 8;
        return ok ? "ok" : "mismatch";
    }
}
