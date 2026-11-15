using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Xunit;

namespace ApexPack.Tests;

/// <summary>
/// 不変条件: ApexPack は利用者の型を握り続けない。collectible な <see cref="AssemblyLoadContext"/> に読み込んだ型を
/// 直列化しても、使い終われば DLL ごと解放できる。
/// </summary>
/// <remarks>
/// ApexPack.Core は既定のコンテキストで共有し、プラグインだけを collectible なコンテキストに読み込む。
/// </remarks>
[Collection(nameof(UnloadabilityTests))] // GC を強制するので他のテストと並列にしない
public class UnloadabilityTests
{
    private static readonly string PluginPath = Path.Combine(AppContext.BaseDirectory, "plugin", "ApexPack.Tests.Plugin.dll");

    /// <summary>既定コンテキストの static。陰性対照で、ここに型を入れると解放されないことを確かめる。</summary>
    private static readonly List<object> s_leak = new();

    private sealed class PluginContext : AssemblyLoadContext
    {
        public PluginContext() : base("ApexPackUnloadTest", isCollectible: true) { }

        // ApexPack.Core などプラグイン以外は既定のコンテキストへ委ねる
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadRunAndUnload(bool leakType, out string result)
    {
        var context = new PluginContext();
        var assembly = context.LoadFromAssemblyPath(PluginPath);
        var entry = assembly.GetType("ApexPack.Tests.Plugin.PluginEntry", throwOnError: true)!;
        result = (string)entry.GetMethod("Run")!.Invoke(null, null)!;
        if (leakType)
        {
            s_leak.Add(assembly.GetType("ApexPack.Tests.Plugin.PluginChild", throwOnError: true)!);
        }
        context.Unload();
        return new WeakReference(context);
    }

    private static bool WaitForUnload(WeakReference reference)
    {
        for (int i = 0; i < 20 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !reference.IsAlive;
    }

    [Fact]
    public void PluginTypes_AreReleased_AfterSerialization()
    {
        Assert.True(File.Exists(PluginPath), $"plugin not found: {PluginPath}");

        var reference = LoadRunAndUnload(leakType: false, out string result);
        Assert.Equal("ok", result);
        Assert.True(WaitForUnload(reference), "ApexPack kept a reference to a type from the collectible AssemblyLoadContext.");
    }

    [Fact]
    public void NegativeControl_StaticTypeReference_PreventsUnload()
    {
        var reference = LoadRunAndUnload(leakType: true, out string result);
        Assert.Equal("ok", result);
        try
        {
            Assert.False(WaitForUnload(reference), "The leak detector failed to observe an intentionally retained Type.");
        }
        finally
        {
            s_leak.Clear();
            Assert.True(WaitForUnload(reference));
        }
    }
}
