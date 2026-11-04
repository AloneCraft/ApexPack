using System;
using System.Collections.Generic;
using Xunit;
using MemoryPack;
using ApexPack;
using ApexPack.Tests.Comparison;

namespace ApexPack.Tests;

// Exact models ported from MemoryPack.Tests.Models
[MemoryPackable(GenerateType.CircularReference)]
[ApexPackable(PreserveReferences = true)]
public partial class MpExactEmployee
{
    [MemoryPackOrder(0)]
    public string? Name { get; set; }

    [MemoryPackOrder(1)]
    public MpExactEmployee? Manager { get; set; }

    [MemoryPackOrder(2)]
    public List<MpExactEmployee>? DirectReports { get; set; }
}

[MemoryPackable(GenerateType.CircularReference)]
[ApexPackable(PreserveReferences = true)]
public partial class MpExactNode
{
    [MemoryPackOrder(0)]
    public MpExactNode? Parent { get; set; }

    [MemoryPackOrder(1)]
    public MpExactNode[]? Children { get; set; }
}

[MemoryPackable]
[ApexPackable]
public partial class MpExactStandardOne
{
    public int One { get; set; }
}

[MemoryPackable]
[ApexPackable]
public partial class MpExactStandardTwo
{
    public int One { get; set; }
    public int Two { get; set; }
}

[MemoryPackable]
[ApexPackable]
public partial struct MpExactUnmanagedStruct
{
    public int MyProperty { get; set; }
}

[MemoryPackable]
[ApexPackable]
public partial class MpExactWithArray
{
    public MpExactStandardOne[]? One { get; set; }
}

public class MemoryPackExactRepoTest
{
    /// <summary>
    /// Port of MemoryPack.Tests.CircularReferenceTest.MicrosoftExample
    /// </summary>
    [Fact]
    public void ExactTest_MicrosoftExample_CircularReference()
    {
        // 1. Setup exact object graph from MemoryPack test
        var tylerMp = new MpExactEmployee { Name = "Tyler Stein" };
        var adrianMp = new MpExactEmployee { Name = "Adrian King" };
        tylerMp.DirectReports = new List<MpExactEmployee> { adrianMp };
        adrianMp.Manager = tylerMp;

        var tylerApex = new MpExactEmployee { Name = "Tyler Stein" };
        var adrianApex = new MpExactEmployee { Name = "Adrian King" };
        tylerApex.DirectReports = new List<MpExactEmployee> { adrianApex };
        adrianApex.Manager = tylerApex;

        // 2. MemoryPack serialization
        byte[] mpBin = MemoryPackSerializer.Serialize(tylerMp);
        var tylerMpRestored = MemoryPackSerializer.Deserialize<MpExactEmployee>(mpBin);

        // 3. ApexPack serialization
        byte[] apexBin = ApexPackSerializer.SerializeWithReferences(tylerApex);
        var tylerApexRestored = ApexPackSerializer.DeserializeWithReferences<MpExactEmployee>(apexBin);

        // 4. Assert both achieve the exact same circular reference resolution!
        Assert.NotNull(tylerMpRestored);
        Assert.NotNull(tylerMpRestored.DirectReports);
        Assert.Same(tylerMpRestored, tylerMpRestored.DirectReports[0].Manager);

        Assert.NotNull(tylerApexRestored);
        Assert.NotNull(tylerApexRestored.DirectReports);
        Assert.Same(tylerApexRestored, tylerApexRestored.DirectReports[0].Manager);
        Assert.Equal("Tyler Stein", tylerApexRestored.Name);
        Assert.Equal("Adrian King", tylerApexRestored.DirectReports[0].Name);
    }

    /// <summary>
    /// Port of MemoryPack.Tests.CircularReferenceTest.NodeTest
    /// </summary>
    [Fact]
    public void ExactTest_NodeTest_ArrayCircularReference()
    {
        var parentMp = new MpExactNode();
        var a1Mp = new MpExactNode { Parent = parentMp };
        var a2Mp = new MpExactNode { Parent = parentMp };
        var a3Mp = new MpExactNode { Parent = parentMp };
        parentMp.Children = new[] { a1Mp, a2Mp, a3Mp };

        var parentApex = new MpExactNode();
        var a1Apex = new MpExactNode { Parent = parentApex };
        var a2Apex = new MpExactNode { Parent = parentApex };
        var a3Apex = new MpExactNode { Parent = parentApex };
        parentApex.Children = new[] { a1Apex, a2Apex, a3Apex };

        byte[] mpBin = MemoryPackSerializer.Serialize(parentMp);
        var mpRestored = MemoryPackSerializer.Deserialize<MpExactNode>(mpBin);

        byte[] apexBin = ApexPackSerializer.SerializeWithReferences(parentApex);
        var apexRestored = ApexPackSerializer.DeserializeWithReferences<MpExactNode>(apexBin);

        Assert.NotNull(mpRestored?.Children);
        foreach (var child in mpRestored.Children)
        {
            Assert.Same(mpRestored, child.Parent);
        }

        Assert.NotNull(apexRestored?.Children);
        Assert.Equal(3, apexRestored.Children.Length);
        foreach (var child in apexRestored.Children)
        {
            Assert.Same(apexRestored, child.Parent);
        }
    }

    /// <summary>
    /// Port of MemoryPack.Tests.StringTest.Utf16
    /// </summary>
    [Fact]
    public void ExactTest_StringUtf16()
    {
        var text = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほわをん";

        // MemoryPack
        byte[] mpBin = MemoryPackSerializer.Serialize(text, MemoryPackSerializerOptions.Utf16);
        string? mpRestored = MemoryPackSerializer.Deserialize<string>(mpBin);
        Assert.Equal(text, mpRestored);

        // ApexPack (native UTF-16 in-memory blit)
        byte[] apexBin = ApexPackSerializer.Serialize(new Person { Name = text });
        var apexRestored = ApexPackSerializer.Deserialize<Person>(apexBin);
        Assert.Equal(text, apexRestored.Name);
    }

    /// <summary>
    /// Port of MemoryPack.Tests.Models.StandardType
    /// </summary>
    [Fact]
    public void ExactTest_StandardModels_ArrayAndStruct()
    {
        var model = new MpExactWithArray
        {
            One = new[]
            {
                new MpExactStandardOne { One = 10 },
                new MpExactStandardOne { One = 20 },
                new MpExactStandardOne { One = 30 }
            }
        };

        byte[] mpBin = MemoryPackSerializer.Serialize(model);
        var mpRestored = MemoryPackSerializer.Deserialize<MpExactWithArray>(mpBin);

        byte[] apexBin = ApexPackSerializer.Serialize(model);
        var apexRestored = ApexPackSerializer.Deserialize<MpExactWithArray>(apexBin);

        Assert.NotNull(apexRestored?.One);
        Assert.Equal(3, apexRestored.One.Length);
        Assert.Equal(10, apexRestored.One[0].One);
        Assert.Equal(20, apexRestored.One[1].One);
        Assert.Equal(30, apexRestored.One[2].One);

        // Struct test
        var s = new MpExactUnmanagedStruct { MyProperty = 999 };
        byte[] sMp = MemoryPackSerializer.Serialize(s);
        byte[] sApex = ApexPackSerializer.Serialize(s);

        Assert.Equal(sizeof(int), sApex.Length); // exactly 4 bytes
        var restoredStruct = ApexPackSerializer.Deserialize<MpExactUnmanagedStruct>(sApex);
        Assert.Equal(999, restoredStruct.MyProperty);
    }
}
