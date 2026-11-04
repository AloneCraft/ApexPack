using System;
using System.Diagnostics;
using ApexPack;
using ApexPack.Buffers;
using ApexPack.Parallel;

namespace ApexPack.Benchmarks;

[ApexPackable]
public partial struct BenchmarkVector
{
    public float X, Y, Z;
}

[ApexPackable]
public partial class BenchmarkEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool FlagA { get; set; }
    public bool FlagB { get; set; }
    public int? NullableCount { get; set; }
    public string? OptionalDescription { get; set; }
}

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== ApexPack High Performance Benchmark Demonstration ===");
        Console.WriteLine($"Hardware acceleration status: AVX2/Vector256={ApexPack.Hardware.HardwareIntrinsics.HasVector256}, BMI2={ApexPack.Hardware.HardwareIntrinsics.HasBmi2}");
        Console.WriteLine();

        // 1. Bit-packing size comparison demonstration
        Console.WriteLine("--- 1. Payload Compaction Demonstration ---");
        var sparseEntity = new BenchmarkEntity
        {
            Id = 1,
            Name = "Player_01",
            FlagA = true,
            FlagB = false,
            NullableCount = null,
            OptionalDescription = null
        };

        byte[] sparseBytes = ApexPackSerializer.Serialize(sparseEntity);
        Console.WriteLine($"Sparse entity (with nulls & bools) serialized size: {sparseBytes.Length} bytes");

        // 2. Multi-core Parallel Scaling Demonstration
        Console.WriteLine();
        Console.WriteLine("--- 2. Multi-Core Parallel Scaling (100,000 Entities) ---");
        const int count = 100_000;
        var entities = new BenchmarkEntity[count];
        for (int i = 0; i < count; i++)
        {
            entities[i] = new BenchmarkEntity
            {
                Id = i,
                Name = $"Entity_{i}",
                FlagA = (i % 2 == 0),
                FlagB = (i % 3 == 0),
                NullableCount = (i % 5 == 0) ? i : null,
                OptionalDescription = (i % 7 == 0) ? "Description" : null
            };
        }

        // Sequential timing
        var sw = Stopwatch.StartNew();
        byte[] seqBytes;
        var writer = new ApexBufferWriter(count * 32);
        try
        {
            for (int i = 0; i < count; i++)
            {
                BenchmarkEntity.Serialize(ref writer, in entities[i]);
            }
            seqBytes = writer.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
        sw.Stop();
        double seqMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[Sequential (1 Core)]   100,000 items in {seqMs:F2} ms (Throughput: {count / (seqMs / 1000):N0} items/sec, {(seqBytes.Length / 1024.0 / 1024.0) / (seqMs / 1000):F2} MB/s)");

        // Parallel timing
        sw.Restart();
        byte[] parBytes = ApexPackParallel.Serialize(entities, chunkSize: 4096);
        sw.Stop();
        double parMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[Parallel (Multi-Core)] 100,000 items in {parMs:F2} ms (Throughput: {count / (parMs / 1000):N0} items/sec, {(parBytes.Length / 1024.0 / 1024.0) / (parMs / 1000):F2} MB/s)");

        double speedup = seqMs / parMs;
        Console.WriteLine($"-> Parallel Speedup: {speedup:F2}x faster!");

        // Parallel Deserialization timing
        sw.Restart();
        var deserialized = ApexPackParallel.Deserialize<BenchmarkEntity>(parBytes, chunkSize: 4096);
        sw.Stop();
        Console.WriteLine($"[Parallel Deserialization] 100,000 items in {sw.Elapsed.TotalMilliseconds:F2} ms (Count: {deserialized.Length:N0})");

        Console.WriteLine();
        Console.WriteLine("All benchmark tests completed successfully!");
    }
}
