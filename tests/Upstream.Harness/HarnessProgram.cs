using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Upstream.Harness;

/// <summary>
/// 比較用の実行入口。<c>dotnet Upstream.OnXxx.dll --harness 出力.json [--filter 文字列] [--no-timing]</c>。
/// xUnit の [Fact] / [Theory] を反射で見つけて 1 件ずつ実行し、テスト中の呼び出しを記録・再生して JSON に書く。
/// 引数なしで起動したときは何もしない(通常のテストは dotnet test / xUnit が実行する)。
/// </summary>
public static class HarnessProgram
{
    public static int Main(string[] args)
    {
        int index = Array.IndexOf(args, "--harness");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.Error.WriteLine("usage: --harness <output.json> [--filter <text>] [--no-timing]");
            return 2;
        }

        string output = args[index + 1];
        int filterIndex = Array.IndexOf(args, "--filter");
        string? filter = filterIndex >= 0 && filterIndex + 1 < args.Length ? args[filterIndex + 1] : null;
        bool timing = !args.Contains("--no-timing");

        var run = new HarnessRun
        {
            Backend = typeof(global::MemoryPack.MemoryPackSerializer).Assembly.GetName().Name == "MemoryPack.Core" ? "MemoryPack" : "ApexPack",
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            StartedUtc = DateTime.UtcNow.ToString("O"),
        };

        // 引数の表示が同じ Theory の行は同じ名前になるので、2 件目から " #2" などを付けて区別する(両側で同じ順に付く)
        var seen = new Dictionary<string, int>();
        var cases = Discover(Assembly.GetEntryAssembly() ?? typeof(HarnessProgram).Assembly)
            .Select(c =>
            {
                int n = seen.TryGetValue(c.Id, out var count) ? count + 1 : 1;
                seen[c.Id] = n;
                return n == 1 ? c : c with { Id = $"{c.Id} #{n}" };
            })
            .Where(c => filter is null || c.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 1. テストを実行し、呼び出しを記録して確認する(再現性・往復)
        var measured = new List<(CallRecord Record, CallResult Result)>();
        int done = 0;
        foreach (var testCase in cases)
        {
            var (result, records) = Execute(testCase);
            run.Tests.Add(result);
            foreach (var record in records)
            {
                var call = record.Check();
                result.Calls.Add(call);
                measured.Add((record, call));
            }
            done++;
            // 進捗(Runner が読む)
            Console.WriteLine($"progress {done}/{cases.Count} {testCase.Id}");
        }

        // 2. 全記録をまとめて暖機し、段階的コンパイルが最適化を終えるのを待ってから測る
        if (timing)
        {
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < measured.Count; i++)
                {
                    measured[i].Record.Warm(calls: 60, milliseconds: 150);
                    if (i % 50 == 0) Console.WriteLine($"progress {i}/{measured.Count} 暖機 {round + 1}/3");
                }
                WaitForJitIdle();
            }
            for (int i = 0; i < measured.Count; i++)
            {
                measured[i].Record.Measure(measured[i].Result);
                Console.WriteLine($"progress {i + 1}/{measured.Count} 計測 {measured[i].Result.Type}");
            }
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(output, JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"done {run.Tests.Count} tests -> {output}");
        return 0;
    }

    /// <summary>
    /// 段階的コンパイルの最適化が落ち着くまで待つ(コンパイル済みメソッド数が 1 秒増えなくなるまで。最大 30 秒)。
    /// 固定の待ち時間では、負荷の高い PC で最適化が終わる前に測ってしまうことがあった。
    /// </summary>
    private static void WaitForJitIdle()
    {
        var limit = Stopwatch.StartNew();
        long last = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: false);
        int quiet = 0;
        while (quiet < 4 && limit.Elapsed < TimeSpan.FromSeconds(30))
        {
            Thread.Sleep(250);
            long now = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: false);
            quiet = now == last ? quiet + 1 : 0;
            last = now;
        }
    }

    private sealed record TestCase(string Id, Type Class, MethodInfo Method, object?[] Arguments, string? Skip);

    private static IEnumerable<TestCase> Discover(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t is not null).ToArray()!;
        }

        foreach (var type in types.Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && !t.ContainsGenericParameters).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).OrderBy(m => m.MetadataToken))
            {
                var fact = method.GetCustomAttribute<FactAttribute>();
                if (fact is null) continue;
                string baseId = $"{type.Name}.{method.Name}";

                if (fact is not TheoryAttribute)
                {
                    yield return new TestCase(baseId, type, method, Array.Empty<object?>(), fact.Skip);
                    continue;
                }

                foreach (var data in method.GetCustomAttributes<DataAttribute>())
                {
                    IEnumerable<object[]> rows;
                    try
                    {
                        rows = data.GetData(method).ToList();
                    }
                    catch (Exception e)
                    {
                        rows = new[] { new object[] { new DataError(e) } };
                    }
                    foreach (var row in rows)
                    {
                        yield return new TestCase($"{baseId}({string.Join(", ", row.Select(Format))})", type, method, row, fact.Skip ?? data.Skip);
                    }
                }
            }
        }
    }

    private sealed record DataError(Exception Exception);

    private static string Format(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        DataError => "<error>",
        Array a => $"{a.GetType().GetElementType()?.Name}[{a.Length}]",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? value.GetType().Name,
    };

    private static (TestResult Result, List<CallRecord> Records) Execute(TestCase testCase)
    {
        var result = new TestResult { Id = testCase.Id, Class = testCase.Class.Name, Method = testCase.Method.Name };
        if (testCase.Skip is not null)
        {
            result.Status = "skip";
            result.SkipReason = testCase.Skip;
            return (result, new List<CallRecord>());
        }

        Recorder.Begin();
        var stopwatch = Stopwatch.StartNew();
        Exception? error = null;
        try
        {
            if (testCase.Arguments.Length == 1 && testCase.Arguments[0] is DataError dataError) throw dataError.Exception;
            InvokeTest(testCase);
        }
        catch (Exception e)
        {
            error = Unwrap(e);
        }
        stopwatch.Stop();
        var records = Recorder.End();

        result.TestMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        if (error is null)
        {
            result.Status = "pass";
        }
        else
        {
            result.Status = "fail";
            result.ErrorType = error.GetType().FullName;
            result.ErrorMessage = error.Message;
            result.Feature = FindFeature(error);
        }

        foreach (var record in records) record.Prepare();
        return (result, Deduplicate(records).ToList());
    }

    private static void InvokeTest(TestCase testCase)
    {
        object? instance = testCase.Method.IsStatic ? null : Activator.CreateInstance(testCase.Class);
        try
        {
            var method = testCase.Method;
            var arguments = ConvertArguments(method, testCase.Arguments);
            if (method.IsGenericMethodDefinition)
            {
                method = method.MakeGenericMethod(InferGenericArguments(method, arguments));
            }
            object? returned = method.Invoke(instance, arguments);
            switch (returned)
            {
                case Task task:
                    task.GetAwaiter().GetResult();
                    break;
                case ValueTask valueTask:
                    valueTask.AsTask().GetAwaiter().GetResult();
                    break;
            }
        }
        finally
        {
            (instance as IDisposable)?.Dispose();
        }
    }

    private static object?[] ConvertArguments(MethodInfo method, object?[] arguments)
    {
        var parameters = method.GetParameters();
        var converted = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i >= arguments.Length)
            {
                converted[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
                continue;
            }
            var value = arguments[i];
            var target = parameters[i].ParameterType;
            if (value is not null && !target.IsGenericParameter && !target.IsInstanceOfType(value))
            {
                var underlying = Nullable.GetUnderlyingType(target) ?? target;
                value = underlying.IsEnum ? Enum.ToObject(underlying, value) : Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
            }
            converted[i] = value;
        }
        return converted;
    }

    private static Type[] InferGenericArguments(MethodInfo method, object?[] arguments)
    {
        var generic = method.GetGenericArguments();
        var inferred = new Type[generic.Length];
        var parameters = method.GetParameters();
        for (int g = 0; g < generic.Length; g++)
        {
            int p = Array.FindIndex(parameters, x => x.ParameterType == generic[g]);
            inferred[g] = p >= 0 && arguments[p] is { } value ? value.GetType() : typeof(object);
        }
        return inferred;
    }

    private static Exception Unwrap(Exception e)
    {
        while (true)
        {
            if (e is TargetInvocationException { InnerException: { } inner }) { e = inner; continue; }
            if (e is AggregateException { InnerExceptions.Count: 1 } aggregate) { e = aggregate.InnerExceptions[0]; continue; }
            return e;
        }
    }

    /// <summary>例外の連鎖から ApexFeatureNotSupportedException を探し、機能名を返す(このアセンブリは ApexPack を参照しない場合もあるので名前で探す)。</summary>
    private static string? FindFeature(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
        {
            if (e.GetType().Name == "ApexFeatureNotSupportedException")
            {
                return e.GetType().GetProperty("Feature")?.GetValue(e) as string ?? e.Message;
            }
            if (e is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    var feature = FindFeature(inner);
                    if (feature is not null) return feature;
                }
            }
        }
        return null;
    }

    /// <summary>直列化の出力をそのまま逆直列化した呼び出しは、直列化側の記録で測るので除く(二重計上を防ぐ)。</summary>
    private static IEnumerable<CallRecord> Deduplicate(List<CallRecord> records)
    {
        var serialized = new List<(string Type, byte[] Bytes)>();
        foreach (var record in records)
        {
            if (record.Op == "serialize")
            {
                if (record.Bytes is not null) serialized.Add((record.TypeName, record.Bytes));
                yield return record;
                continue;
            }
            if (record.Bytes is not null && serialized.Any(s => s.Type == record.TypeName && s.Bytes.AsSpan().SequenceEqual(record.Bytes)))
            {
                continue;
            }
            yield return record;
        }
    }
}
