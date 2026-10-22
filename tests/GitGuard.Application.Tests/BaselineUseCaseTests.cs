using System.Text.Json;
using FluentAssertions;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Tests;

/// <summary>
/// 基线文件用例测试（FR-061）：生成写入、默认消费与历史噪声放行、
/// 指纹内容变化后重新阻断，以及基线文件读取 / 写入失败的 FailClosed 分支。
/// </summary>
public sealed class BaselineUseCaseTests : IDisposable
{
    private const string HEAD_SHA = "hhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhh";
    private const string RULE_ID = "GGD/SECRET/AWS-AKID";

    private readonly string _repositoryRoot;

    public BaselineUseCaseTests()
    {
        _repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            "gitguard-baseline-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repositoryRoot);
    }

    private string DefaultBaselinePath => Path.Combine(_repositoryRoot, ".gitguard-baseline.json");

    [Fact]
    public async Task ExecuteAsync_BaselinePath_WritesBaselineWithoutSuppressingCurrentFindings()
    {
        var git = CreateGit();
        var detector = new StubDetector("secret", _ => new List<Finding>
        {
            FindingTestData.Create(RULE_ID, Severity.Critical),
        });
        var useCase = CreateUseCase(git, detector);
        string baselinePath = Path.Combine(_repositoryRoot, "custom-baseline.json");

        ScanOutcome outcome = await useCase.ExecuteAsync(
            new ScanAllOptions { BaselinePath = baselinePath },
            TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.ExitCode.Should().Be(1);
        outcome.Findings.Should().ContainSingle();
        BaselineSummary summary = outcome.BaselineSummary.Should().BeOfType<BaselineSummary>().Subject;
        summary.Path.Should().Be(baselinePath);
        summary.Generated.Should().BeTrue();
        summary.Entries.Should().Be(1);
        summary.SuppressedFindings.Should().Be(0);

        File.Exists(baselinePath).Should().BeTrue();
        using var document = JsonDocument.Parse(File.ReadAllText(baselinePath));
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.baseline.v1");
        JsonElement entry = root.GetProperty("entries").EnumerateArray().Single();
        entry.GetProperty("fingerprint").GetString().Should().Be(new string('a', 64));
        entry.GetProperty("rule_id").GetString().Should().Be(RULE_ID);
        entry.GetProperty("file_path").GetString().Should().Be("config/app.env");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultBaseline_SuppressesKnownFingerprintAndReturnsWarned()
    {
        var detector = new StubDetector("secret", _ => new List<Finding>
        {
            FindingTestData.Create(RULE_ID, Severity.Critical),
        });
        var useCase = CreateUseCase(CreateGit(), detector);

        ScanOutcome generated = await useCase.ExecuteAsync(
            new ScanAllOptions { BaselinePath = DefaultBaselinePath },
            TestContext.Current.CancellationToken);
        generated.Result.Should().Be(ScanResult.Blocked);

        ScanOutcome consumed = await useCase.ExecuteAsync(ScanAllOptions.Default, TestContext.Current.CancellationToken);

        consumed.Result.Should().Be(ScanResult.Warned);
        consumed.ExitCode.Should().Be(0);
        consumed.Findings.Should().ContainSingle();
        PolicyDecision decision = consumed.Decisions[consumed.Findings[0]];
        decision.Action.Should().Be(PolicyAction.Allow);
        decision.IgnoredByAllowlist.Should().BeFalse();
        decision.IgnoredByBaseline.Should().BeTrue();

        BaselineSummary summary = consumed.BaselineSummary.Should().BeOfType<BaselineSummary>().Subject;
        summary.Path.Should().Be(DefaultBaselinePath);
        summary.Generated.Should().BeFalse();
        summary.Entries.Should().Be(1);
        summary.SuppressedFindings.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ChangedFingerprint_StillBlocksDespiteBaseline()
    {
        var baselineDetector = new StubDetector("secret", _ => new List<Finding>
        {
            CreateFinding('a'),
        });
        var useCase = CreateUseCase(CreateGit(), baselineDetector);
        await useCase.ExecuteAsync(
            new ScanAllOptions { BaselinePath = DefaultBaselinePath },
            TestContext.Current.CancellationToken);

        var changedDetector = new StubDetector("secret", _ => new List<Finding>
        {
            CreateFinding('b'),
        });
        ScanOutcome outcome = await CreateUseCase(CreateGit(), changedDetector)
            .ExecuteAsync(ScanAllOptions.Default, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.ExitCode.Should().Be(1);
        outcome.BaselineSummary.Should().BeOfType<BaselineSummary>()
            .Which.SuppressedFindings.Should().Be(0);
        outcome.Decisions[outcome.Findings[0]].Action.Should().Be(PolicyAction.Block);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateFingerprints_DeduplicatedInBaselineFile()
    {
        var detector = new StubDetector("secret", _ => new List<Finding>
        {
            FindingTestData.Create(RULE_ID, Severity.Critical, path: "config/a.env"),
            FindingTestData.Create(RULE_ID, Severity.Critical, path: "config/b.env"),
        });
        var useCase = CreateUseCase(CreateGit(), detector);

        ScanOutcome outcome = await useCase.ExecuteAsync(
            new ScanAllOptions { BaselinePath = DefaultBaselinePath },
            TestContext.Current.CancellationToken);

        outcome.Findings.Should().HaveCount(2);
        outcome.BaselineSummary.Should().BeOfType<BaselineSummary>().Which.Entries.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedDefaultBaseline_ThrowsBaselineInvalid()
    {
        File.WriteAllText(DefaultBaselinePath, "{ not-json");
        var useCase = CreateUseCase(CreateGit(), new StubDetector("secret", _ => Array.Empty<Finding>()));

        Func<Task> act = () => useCase.ExecuteAsync(ScanAllOptions.Default, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<BaselineException>()
            .Where(exception => exception.ErrorCode == BaselineException.BASELINE_INVALID);
    }

    [Fact]
    public async Task ExecuteAsync_UnwritableBaselinePath_ThrowsBaselineWriteFailed()
    {
        string baselinePath = Path.Combine(_repositoryRoot, "missing-directory", "baseline.json");
        var useCase = CreateUseCase(
            CreateGit(),
            new StubDetector("secret", _ => new List<Finding>
            {
                FindingTestData.Create(RULE_ID, Severity.Critical),
            }));

        Func<Task> act = () => useCase.ExecuteAsync(
            new ScanAllOptions { BaselinePath = baselinePath },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<BaselineException>()
            .Where(exception => exception.ErrorCode == BaselineException.BASELINE_WRITE_FAILED);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-baseline.json")]
    public async Task ExecuteAsync_InvalidBaselinePath_ThrowsArgumentException(string baselinePath)
    {
        var useCase = CreateUseCase(CreateGit(), new StubDetector("secret", _ => Array.Empty<Finding>()));

        Func<Task> act = () => useCase.ExecuteAsync(
            new ScanAllOptions { BaselinePath = baselinePath },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论
        }
        catch (UnauthorizedAccessException)
        {
            // 同上：偶发句柄竞态交给系统清理
        }
    }

    private static Finding CreateFinding(char fingerprintChar) =>
        Finding.Create(
            RULE_ID,
            "config/app.env",
            1,
            Severity.Critical,
            Fingerprint.FromHex(new string(fingerprintChar, 64)),
            SnippetMasked.Create(FindingTestData.MASKED_SNIPPET));

    private static ScanAllUseCase CreateUseCase(FakeGitAccess git, IDetector detector) =>
        new ScanAllUseCase(
            git,
            new List<IDetector> { detector },
            new RecordingAuditStore(),
            new FixedClock(new DateTimeOffset(2026, 10, 22, 8, 30, 0, TimeSpan.Zero)));

    private FakeGitAccess CreateGit() =>
        new FakeGitAccess(
            _repositoryRoot,
            headCommitSha: () => HEAD_SHA,
            fullTreeDiff: _ => DiffSamples.SINGLE_ADDED_LINE);
}
