using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

public sealed class ScanRunTests
{
    [Fact]
    public void Create_CleanWithoutFindings_PopulatesFields()
    {
        var run = ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Clean, 0);

        run.Id.Should().NotBe(Guid.Empty);
        run.Id.Version.Should().Be(7);
        run.RepoPath.Should().Be(TestData.SampleRepoPath);
        run.ScanType.Should().Be(ScanType.PreCommit);
        run.StartedAt.Should().Be(TestData.SampleUtcNow);
        run.DurationMs.Should().Be(12);
        run.Result.Should().Be(ScanResult.Clean);
        run.ExitCode.Should().Be(0);
        run.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Create_NullDuration_Accepted()
    {
        var run = ScanRun.Create(TestData.SampleRepoPath, ScanType.Manual, TestData.SampleUtcNow, null, ScanResult.Clean, 0);

        run.DurationMs.Should().BeNull();
    }

    [Fact]
    public void Create_CleanWithFindings_Throws()
    {
        Action act = () => ScanRun.Create(
            TestData.SampleRepoPath,
            ScanType.PreCommit,
            TestData.SampleUtcNow,
            12,
            ScanResult.Clean,
            0,
            new Finding[] { TestData.CreateFinding() });

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.1*");
    }

    [Fact]
    public void Create_CleanWithNonZeroExitCode_Throws()
    {
        Action act = () => ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Clean, 1);

        act.Should().Throw<InvariantViolationException>().WithMessage("*FR-020*");
    }

    [Fact]
    public void Create_WarnedWithFindings_Accepted()
    {
        var run = ScanRun.Create(
            TestData.SampleRepoPath,
            ScanType.PreCommit,
            TestData.SampleUtcNow,
            12,
            ScanResult.Warned,
            0,
            new Finding[] { TestData.CreateFinding() });

        run.Result.Should().Be(ScanResult.Warned);
        run.Findings.Should().HaveCount(1);
    }

    [Fact]
    public void Create_WarnedWithNonZeroExitCode_Throws()
    {
        Action act = () => ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Warned, 1);

        act.Should().Throw<InvariantViolationException>().WithMessage("*FR-020*");
    }

    [Fact]
    public void Create_BlockedWithFindingAndExitCodeOne_Accepted()
    {
        var run = ScanRun.Create(
            TestData.SampleRepoPath,
            ScanType.PreCommit,
            TestData.SampleUtcNow,
            12,
            ScanResult.Blocked,
            1,
            new Finding[] { TestData.CreateFinding() });

        run.Result.Should().Be(ScanResult.Blocked);
        run.ExitCode.Should().Be(1);
    }

    [Fact]
    public void Create_BlockedWithoutFindings_Throws()
    {
        Action act = () => ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Blocked, 1);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.1*");
    }

    [Fact]
    public void Create_BlockedWithExitCodeZero_Throws()
    {
        Action act = () => ScanRun.Create(
            TestData.SampleRepoPath,
            ScanType.PreCommit,
            TestData.SampleUtcNow,
            12,
            ScanResult.Blocked,
            0,
            new Finding[] { TestData.CreateFinding() });

        act.Should().Throw<InvariantViolationException>().WithMessage("*FR-020*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    public void Create_FailedWithExitCodeBelowTen_Throws(int exitCode)
    {
        Action act = () => ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Failed, exitCode);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-03*");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(13)]
    public void Create_FailedWithExitCodeAtLeastTen_Accepted(int exitCode)
    {
        var run = ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Failed, exitCode);

        run.Result.Should().Be(ScanResult.Failed);
        run.ExitCode.Should().Be(exitCode);
    }

    [Fact]
    public void Create_BypassWithFindings_Throws()
    {
        Action act = () => ScanRun.Create(
            TestData.SampleRepoPath,
            ScanType.Bypass,
            TestData.SampleUtcNow,
            12,
            ScanResult.Warned,
            0,
            new Finding[] { TestData.CreateFinding() });

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.2*");
    }

    [Fact]
    public void Create_BypassWithoutFindings_Accepted()
    {
        var run = ScanRun.Create(TestData.SampleRepoPath, ScanType.Bypass, TestData.SampleUtcNow, 12, ScanResult.Warned, 0);

        run.ScanType.Should().Be(ScanType.Bypass);
    }

    [Fact]
    public void Create_FindingsContainingNull_Throws()
    {
        Action act = () => ScanRun.Create(
            TestData.SampleRepoPath,
            ScanType.PreCommit,
            TestData.SampleUtcNow,
            12,
            ScanResult.Warned,
            0,
            new Finding[] { null! });

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.1*");
    }

    [Fact]
    public void Create_RelativeRepoPath_Throws()
    {
        Action act = () => ScanRun.Create("repo", ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Clean, 0);

        act.Should().Throw<InvariantViolationException>().WithMessage("*绝对路径*");
    }

    [Fact]
    public void Create_WhitespaceRepoPath_Throws()
    {
        Action act = () => ScanRun.Create("   ", ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Clean, 0);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.1*");
    }

    [Fact]
    public void Create_TrailingSeparator_IsTrimmed()
    {
        string withSeparator = TestData.SampleRepoPath + Path.DirectorySeparatorChar;

        var run = ScanRun.Create(withSeparator, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Clean, 0);

        run.RepoPath.Should().Be(TestData.SampleRepoPath);
    }

    [Fact]
    public void Create_NonUtcStartedAt_Throws()
    {
        var local = new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.FromHours(8));

        Action act = () => ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, local, 12, ScanResult.Clean, 0);

        act.Should().Throw<InvariantViolationException>().WithMessage("*UTC*");
    }

    [Fact]
    public void Create_NegativeDuration_Throws()
    {
        Action act = () => ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, -1, ScanResult.Clean, 0);

        act.Should().Throw<InvariantViolationException>().WithMessage("*不能为负*");
    }

    [Fact]
    public void Create_UndefinedScanType_Throws()
    {
        Action act = () => ScanRun.Create(TestData.SampleRepoPath, (ScanType)99, TestData.SampleUtcNow, 12, ScanResult.Clean, 0);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.2*");
    }

    [Fact]
    public void Create_UndefinedScanResult_Throws()
    {
        Action act = () => ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, 12, (ScanResult)99, 0);

        act.Should().Throw<InvariantViolationException>().WithMessage("*未知的 ScanResult*");
    }

    [Fact]
    public void Create_NullRepoPath_ThrowsArgumentNullException()
    {
        Action act = () => ScanRun.Create(null!, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Clean, 0);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equality_SameContentDifferentInstances_DistinctIdentity()
    {
        var first = ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Clean, 0);
        var second = ScanRun.Create(TestData.SampleRepoPath, ScanType.PreCommit, TestData.SampleUtcNow, 12, ScanResult.Clean, 0);

        first.Equals(first).Should().BeTrue();
        first.Should().NotBe(second);
    }
}
