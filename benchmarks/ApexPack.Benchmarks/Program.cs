using System;
using System.Diagnostics;
using MemoryPack;
using ApexPack;
using ApexPack.Buffers;
using ApexPack.Parallel;

namespace ApexPack.Benchmarks;

[MemoryPackable]
[ApexPackable]
public partial class Person
{
    public int Age { get; set; }
    public string Name { get; set; } = "";
}

[MemoryPackable]
[ApexPackable]
public partial class SparseData
{
    public int Id { get; set; }
    public bool Flag1 { get; set; }
    public bool Flag2 { get; set; }
    public bool Flag3 { get; set; }
    public bool Flag4 { get; set; }
    public int? NullableInt1 { get; set; }
    public int? NullableInt2 { get; set; }
    public string? OptionalDescription { get; set; }
}

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("==========================================================================");
        Console.WriteLine("          ApexPack vs MemoryPack Head-to-Head Performance Benchmark        ");
        Console.WriteLine("==========================================================================");
        Console.WriteLine($"Hardware SIMD Acceleration: AVX2/Vector256={ApexPack.Hardware.HardwareIntrinsics.HasVector256}, BMI2={ApexPack.Hardware.HardwareIntrinsics.HasBmi2}");
        Console.WriteLine();

        // -----------------------------------------------------------------------
        // Benchmark 1: Person (Single Object Serialization & Deserialization)
        // -----------------------------------------------------------------------
        Console.WriteLine("--- Benchmark 1: Single Object (Person: Age, Name) x 1,000,000 iterations ---");
        var person = new Person { Age = 32, Name = "Yoshifumi Kawai" };

        const int singleIter = 1_000_000;

        // Warmup
        MemoryPackSerializer.Deserialize<Person>(MemoryPackSerializer.Serialize(person));
        ApexPackSerializer.Deserialize<Person>(ApexPackSerializer.Serialize(person));

        // MemoryPack Single Timing
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < singleIter; i++)
        {
            var bytes = MemoryPackSerializer.Serialize(person);
            _ = MemoryPackSerializer.Deserialize<Person>(bytes);
        }
        sw.Stop();
        double mpSingleMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[MemoryPack] {singleIter:N0} roundtrips in {mpSingleMs:F2} ms ({(singleIter / (mpSingleMs / 1000)):N0} ops/sec)");

        // ApexPack Single Timing (using static abstract inlined dispatch)
        sw.Restart();
        for (int i = 0; i < singleIter; i++)
        {
            var bytes = ApexPackSerializer.Serialize(person);
            _ = ApexPackSerializer.Deserialize<Person>(bytes);
        }
        sw.Stop();
        double apexSingleMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[ApexPack]   {singleIter:N0} roundtrips in {apexSingleMs:F2} ms ({(singleIter / (apexSingleMs / 1000)):N0} ops/sec)");
        Console.WriteLine($"-> Speedup: {mpSingleMs / apexSingleMs:F2}x");
        Console.WriteLine();

        // -----------------------------------------------------------------------
        // Benchmark 2: Sparse Nullable / Boolean Bit-Packing Comparison
        // -----------------------------------------------------------------------
        Console.WriteLine("--- Benchmark 2: Bit-Packing Payload Compaction (10,000 Sparse Objects) ---");
        const int sparseCount = 10_000;
        var sparseList = new SparseData[sparseCount];
        for (int i = 0; i < sparseCount; i++)
        {
            sparseList[i] = new SparseData
            {
                Id = i,
                Flag1 = (i % 2 == 0),
                Flag2 = false,
                Flag3 = (i % 3 == 0),
                Flag4 = false,
                NullableInt1 = (i % 4 == 0) ? i : null,
                NullableInt2 = null,
                OptionalDescription = (i % 5 == 0) ? "Description" : null
            };
        }

        byte[] mpSparseBytes = MemoryPackSerializer.Serialize(sparseList);
        byte[] apexSparseBytes = ApexPackParallel.Serialize(sparseList, chunkSize: 2048);

        Console.WriteLine($"MemoryPack Total Payload Size: {mpSparseBytes.Length:N0} bytes ({mpSparseBytes.Length / 1024.0:F1} KB)");
        Console.WriteLine($"ApexPack   Total Payload Size: {apexSparseBytes.Length:N0} bytes ({apexSparseBytes.Length / 1024.0:F1} KB)");
        double sizeReduction = (1.0 - (double)apexSparseBytes.Length / mpSparseBytes.Length) * 100.0;
        Console.WriteLine($"-> Bit-Packing Size Reduction: {sizeReduction:F1}% smaller payload than MemoryPack!");
        Console.WriteLine();

        // -----------------------------------------------------------------------
        // Benchmark 3: Large Collection (100,000 Persons)
        // -----------------------------------------------------------------------
        Console.WriteLine("--- Benchmark 3: Large Collection (100,000 Persons) Serialization & Deserialization ---");
        const int largeCount = 100_000;
        var largePersons = new Person[largeCount];
        for (int i = 0; i < largeCount; i++)
        {
            largePersons[i] = new Person { Age = i % 100, Name = $"Person_{i}" };
        }

        // MemoryPack (Sequential)
        sw.Restart();
        byte[] mpLargeBytes = MemoryPackSerializer.Serialize(largePersons);
        var mpRestored = MemoryPackSerializer.Deserialize<Person[]>(mpLargeBytes);
        sw.Stop();
        double mpLargeMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[MemoryPack (Sequential)]    100,000 items in {mpLargeMs:F2} ms ({(largeCount / (mpLargeMs / 1000)):N0} items/sec)");

        // ApexPack (Sequential Fast-Path)
        sw.Restart();
        byte[] apexSeqBytes;
        var seqWriter = new ApexBufferWriter(largeCount * 32);
        try
        {
            ApexPackSerializer.SerializeArray(ref seqWriter, largePersons);
            apexSeqBytes = seqWriter.ToArray();
        }
        finally
        {
            seqWriter.Dispose();
        }
        var seqReader = new ApexSpanReader(apexSeqBytes);
        var apexSeqRestored = ApexPackSerializer.DeserializeArray<Person, ApexSpanReader>(ref seqReader);
        sw.Stop();
        double apexSeqMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[ApexPack (Sequential)]      100,000 items in {apexSeqMs:F2} ms ({(largeCount / (apexSeqMs / 1000)):N0} items/sec)");

        // ApexPack (Parallel Multi-Core Pipeline)
        sw.Restart();
        byte[] apexLargeBytes = ApexPackParallel.Serialize(largePersons, chunkSize: 8192);
        var apexRestored = ApexPackParallel.Deserialize<Person>(apexLargeBytes, chunkSize: 8192);
        sw.Stop();
        double apexLargeMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[ApexPack (Parallel APXP)]   100,000 items in {apexLargeMs:F2} ms ({(largeCount / (apexLargeMs / 1000)):N0} items/sec)");

        Console.WriteLine();
        Console.WriteLine("==========================================================================");
        Console.WriteLine("All head-to-head comparisons completed successfully.");
    }
}
