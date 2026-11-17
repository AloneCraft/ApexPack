using Upstream.Harness;
using Upstream.Runner;
using Xunit;

namespace ApexPack.Tests;

public class UpstreamComparisonTests
{
    [Fact]
    public void Compare_UsesFirstResultPerIdAndKeepsUnmatchedTestsInOrder()
    {
        var memoryPack = new HarnessRun
        {
            Tests =
            [
                new() { Id = "Example.Left", Status = "pass" },
                new() { Id = "Example.Match", Status = "pass" },
                new() { Id = "Example.Match", Status = "skip" },
            ],
        };
        var apexPack = new HarnessRun
        {
            Tests =
            [
                new() { Id = "Example.Right", Status = "pass" },
                new() { Id = "Example.Match", Status = "pass" },
                new() { Id = "Example.Match", Status = "skip" },
                new() { Id = "Example.right", Status = "pass" },
            ],
        };

        var rows = UpstreamComparison.Compare(memoryPack, apexPack, false).Rows;

        Assert.Equal(new[] { "Example.Left", "Example.Match", "Example.Right", "Example.right" }, rows.Select(row => row.Id));
        Assert.Equal(new[] { Verdicts.Failed, Verdicts.Match, Verdicts.Failed, Verdicts.Failed }, rows.Select(row => row.Verdict));
        Assert.Equal("なし", rows[0].ApexPackStatus);
        Assert.Equal("なし", rows[2].MemoryPackStatus);
    }

    [Fact]
    public void FailedReplay_DoesNotHideOtherMatchedCalls()
    {
        var bad = new CallResult { Op = "serialize", Type = "Node", ReplayError = "error" };
        var good = new CallResult { Op = "deserialize", Type = "Node", Bytes = 10, SerializeNs = 5, DeserializeNs = 3 };
        var test = new TestResult { Id = "Example.Test", Class = "Example", Method = "Test", Status = "pass", Calls = new() { bad, good } };
        var report = UpstreamComparison.Compare(new HarnessRun { Tests = new() { test } }, new HarnessRun { Tests = new() { test } }, true);
        var row = Assert.Single(report.Rows);
        Assert.Equal(1, row.Calls);
        Assert.Equal(10, row.MemoryPackBytes);
        Assert.Equal(5, row.MemoryPackSerializeNs);
    }

    [Fact]
    public void DifferentCallTypes_AreNotCompared()
    {
        var m = new TestResult { Id = "Example.Test", Class = "Example", Method = "Test", Status = "pass", Calls = new() { new CallResult { Type = "A", Op = "serialize", Bytes = 1 } } };
        var a = new TestResult { Id = m.Id, Class = m.Class, Method = m.Method, Status = "pass", Calls = new() { new CallResult { Type = "B", Op = "serialize", Bytes = 1 } } };
        var row = Assert.Single(UpstreamComparison.Compare(new HarnessRun { Tests = new() { m } }, new HarnessRun { Tests = new() { a } }, true).Rows);
        Assert.Null(row.MemoryPackBytes);
    }
}
