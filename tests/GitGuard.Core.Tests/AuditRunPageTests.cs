using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

/// <summary>审计运行摘要与分页结果不变式（06-API接口设计 §5.6）。</summary>
public sealed class AuditRunPageTests
{
    [Fact]
    public void Summary_ValidFields_NormalizesTrailingSeparator()
    {
        var summary = new AuditRunSummary(
            Guid.CreateVersion7(),
            TestData.SampleRepoPath + Path.DirectorySeparatorChar,
            ScanType.PrePush,
            TestData.SampleUtcNow,
            18,
            ScanResult.Blocked,
            1,
            3);

        summary.RepoPath.Should().Be(TestData.SampleRepoPath);
        summary.FindingCount.Should().Be(3);
    }

    [Fact]
    public void Summary_NegativeFindingCount_Throws()
    {
        Action act = () => _ = new AuditRunSummary(
            Guid.CreateVersion7(),
            TestData.SampleRepoPath,
            ScanType.PreCommit,
            TestData.SampleUtcNow,
            null,
            ScanResult.Clean,
            0,
            -1);

        act.Should().Throw<InvariantViolationException>().WithMessage("*FindingCount*");
    }

    [Fact]
    public void Summary_EmptyRunId_Throws()
    {
        Action act = () => _ = new AuditRunSummary(
            Guid.Empty,
            TestData.SampleRepoPath,
            ScanType.PreCommit,
            TestData.SampleUtcNow,
            null,
            ScanResult.Clean,
            0,
            0);

        act.Should().Throw<InvariantViolationException>().WithMessage("*标识*");
    }

    [Fact]
    public void Page_StoresRunsAndTotal()
    {
        var summary = CreateSummary();

        var page = new AuditRunPage(new[] { summary }, totalCount: 12);

        page.Runs.Should().ContainSingle().Which.Should().Be(summary);
        page.TotalCount.Should().Be(12);
    }

    [Fact]
    public void Page_EmptyPageWithNonZeroTotal_Accepted()
    {
        var page = new AuditRunPage(Array.Empty<AuditRunSummary>(), totalCount: 5);

        page.Runs.Should().BeEmpty();
        page.TotalCount.Should().Be(5);
    }

    [Fact]
    public void Page_TotalSmallerThanPageCount_Throws()
    {
        Action act = () => _ = new AuditRunPage(new[] { CreateSummary() }, totalCount: 0);

        act.Should().Throw<InvariantViolationException>().WithMessage("*TotalCount*");
    }

    [Fact]
    public void Page_NullRuns_Throws()
    {
        Action act = () => _ = new AuditRunPage(null!, totalCount: 0);

        act.Should().Throw<ArgumentNullException>();
    }

    private static AuditRunSummary CreateSummary() => new AuditRunSummary(
        Guid.CreateVersion7(),
        TestData.SampleRepoPath,
        ScanType.Manual,
        TestData.SampleUtcNow,
        5,
        ScanResult.Clean,
        0,
        0);
}
