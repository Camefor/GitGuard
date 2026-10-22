using FluentAssertions;
using GitGuard.Application.Audit;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Tests;

/// <summary>
/// 审计运行详情用例测试（<c>06-API接口设计</c> §4.9 / FR-023）：按运行 ID 返回聚合与 Finding 明细、
/// 未知 ID 返回 <see langword="null"/>、空 GUID / 空存储参数校验。
/// </summary>
public sealed class AuditViewUseCaseTests
{
    private static readonly DateTimeOffset _startedAt = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Execute_ExistingRun_ReturnsAggregateWithFindings()
    {
        string repoPath = Path.Combine(Path.GetTempPath(), "gitguard-view-tests");
        var finding = Finding.Create(
            "GGD/SECRET/AWS-AKID",
            "config/app.env",
            3,
            Severity.Critical,
            Fingerprint.FromHex(new string('b', 64)),
            SnippetMasked.Create("AKIA****************"));
        var run = ScanRun.Create(
            repoPath,
            ScanType.PrePush,
            _startedAt,
            42,
            ScanResult.Blocked,
            1,
            new[] { finding });
        var store = new RecordingAuditStore();
        store.SaveScanRun(
            run,
            new Dictionary<Finding, PolicyDecision>
            {
                [finding] = new PolicyDecision(PolicyAction.Block, false, Severity.Critical),
            });

        ScanRun? result = new AuditViewUseCase(store).Execute(run.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(run.Id);
        result.ScanType.Should().Be(ScanType.PrePush);
        result.DurationMs.Should().Be(42);
        result.Findings.Should().ContainSingle().Which.Id.Should().Be(finding.Id);
        result.Findings[0].SnippetMasked.Value.Should().Be("AKIA****************");
        result.Findings[0].Fingerprint.Hex.Should().Be(new string('b', 64));
    }

    [Fact]
    public void Execute_UnknownRunId_ReturnsNull()
    {
        var useCase = new AuditViewUseCase(new RecordingAuditStore());

        ScanRun? result = useCase.Execute(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public void Execute_EmptyGuid_ThrowsArgumentException()
    {
        var useCase = new AuditViewUseCase(new RecordingAuditStore());

        Action act = () => useCase.Execute(Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("runId");
    }

    [Fact]
    public void Constructor_NullStore_ThrowsArgumentNullException()
    {
        Action act = () => _ = new AuditViewUseCase(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
