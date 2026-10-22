using FluentAssertions;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Detection.Adapters;

namespace GitGuard.Detection.Tests;

/// <summary>
/// Gitleaks 适配器契约测试（FR-090 / ADR-0003 / <c>模块设计/Secret.md</c> §4）：
/// 变更行暂存 → <c>detect --no-git --report-format json</c> → GLK/ 映射 → 原始行号还原 → 临时目录清理；
/// 以及全部 FailClosed 分支（非零退出、报告缺失 / 非法、路径逃逸、禁用规则过滤）。
/// </summary>
public sealed class GitleaksAdapterTests
{
    private const string RAW_SECRET = "glk-test-secret-value-0001";

    private const string SINGLE_FINDING_REPORT = """
        [
          {
            "RuleID": "aws-access-token",
            "Description": "AWS Access Token",
            "File": "src/app.env",
            "StartLine": 2,
            "EndLine": 2,
            "Secret": "glk-test-secret-value-0001",
            "Match": "aws_key = glk-test-secret-value-0001"
          }
        ]
        """;

    private static readonly string[] _expectedArguments = { "detect", "--no-git", "--report-format", "json" };

    private static readonly string[] _disabledRuleIds = { "GGD/SECRET/AWS-AKID", "glk/secret/aws-access-token" };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task DetectAsync_MapsReportToFindingAndRestoresOriginalLineNumber(int exitCode)
    {
        var lines = new[]
        {
            new ChangedLine(5, "placeholder"),
            new ChangedLine(9, "aws_key = " + RAW_SECRET),
        };
        var change = new FileChange("src/app.env", lines);
        FakeGitleaksProcessRunner runner = Reporting(SINGLE_FINDING_REPORT, exitCode);
        GitleaksAdapter adapter = CreateAdapter(runner);

        List<Finding> findings = await TestSupport.CollectAsync(adapter, change);

        Finding finding = findings.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("GLK/SECRET/AWS-ACCESS-TOKEN");
        finding.FilePath.Should().Be("src/app.env");
        finding.LineNumber.Should().Be(9, "报告 StartLine 是暂存文件行序号，必须映射回 diff 原始行号");
        finding.Severity.Should().Be(Severity.High, "Gitleaks 报告不携带严重度，统一按 High 处理");
        finding.SnippetMasked.Value.Should().NotContain(RAW_SECRET, "DR-02：Finding 不得包含敏感原文");
        finding.Fingerprint.Hex.Should().HaveLength(64);

        runner.StagedFiles.Should().ContainKey("src/app.env");
        runner.StagedFiles["src/app.env"].Should().Be("placeholder\naws_key = " + RAW_SECRET + "\n");
        runner.LastArguments.Should().Contain(_expectedArguments);
        Directory.Exists(Path.GetDirectoryName(runner.ReportPath!)).Should().BeFalse("临时目录内含原文，必须在 finally 中清理");
    }

    [Fact]
    public async Task DetectAsync_DisabledRules_FiltersRawAndNamespacedIds()
    {
        var lines = new[]
        {
            new ChangedLine(1, "aws_key = " + RAW_SECRET),
            new ChangedLine(2, "api_key = 0123456789abcdef0123456789abcdef"),
        };
        var change = new FileChange("src/app.env", lines);
        const string report = """
            [
              {
                "RuleID": "aws-access-token",
                "StartLine": 1,
                "Secret": "glk-test-secret-value-0001"
              },
              {
                "RuleID": "generic-api-key",
                "StartLine": 2,
                "Secret": "0123456789abcdef0123456789abcdef"
              }
            ]
            """;
        FakeGitleaksProcessRunner runner = Reporting(report);
        GitleaksAdapter adapter = CreateAdapter(runner, _disabledRuleIds);

        List<Finding> findings = await TestSupport.CollectAsync(adapter, change);

        Finding finding = findings.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("GLK/SECRET/GENERIC-API-KEY", "GGD 条目由内置检测器消费，GLK 条目忽略大小写命中适配器");
    }

    [Fact]
    public async Task DetectAsync_NonZeroExitCode_ThrowsDetectionException()
    {
        FakeGitleaksProcessRunner runner = Reporting(SINGLE_FINDING_REPORT, exitCode: 2);
        GitleaksAdapter adapter = CreateAdapter(runner);

        Func<Task> act = async () => await TestSupport.CollectAsync(adapter, CreateSingleLineChange());

        await act.Should().ThrowAsync<DetectionException>().WithMessage("*退出码 2*");
    }

    [Theory]
    [InlineData("{ not-json")]
    [InlineData("{\"RuleID\":\"aws-access-token\"}")]
    [InlineData("[42]")]
    public async Task DetectAsync_InvalidReportStructure_ThrowsDetectionExceptionAndCleansTempDirectory(string report)
    {
        FakeGitleaksProcessRunner runner = Reporting(report);
        GitleaksAdapter adapter = CreateAdapter(runner);

        Func<Task> act = async () => await TestSupport.CollectAsync(adapter, CreateSingleLineChange());

        await act.Should().ThrowAsync<DetectionException>();
        Directory.Exists(Path.GetDirectoryName(runner.ReportPath!)).Should().BeFalse();
    }

    [Fact]
    public async Task DetectAsync_MissingReportWithZeroExitCode_IsTreatedAsNoFindings()
    {
        var runner = new FakeGitleaksProcessRunner((_, _) => new GitleaksProcessResult(0, string.Empty, string.Empty));
        GitleaksAdapter adapter = CreateAdapter(runner);

        List<Finding> findings = await TestSupport.CollectAsync(adapter, CreateSingleLineChange());

        findings.Should().BeEmpty("gitleaks 无命中时不生成报告文件，退出码 0 表示扫描成功");
    }

    [Fact]
    public async Task DetectAsync_MissingReportWithLeakExitCode_ThrowsDetectionException()
    {
        var runner = new FakeGitleaksProcessRunner((_, _) => new GitleaksProcessResult(1, string.Empty, string.Empty));
        GitleaksAdapter adapter = CreateAdapter(runner);

        Func<Task> act = async () => await TestSupport.CollectAsync(adapter, CreateSingleLineChange());

        await act.Should().ThrowAsync<DetectionException>().WithMessage("*未生成 JSON 报告*");
    }

    [Fact]
    public async Task DetectAsync_PathTraversal_IsRejectedBeforeStartingProcess()
    {
        var change = new FileChange("../escape.txt", new[] { new ChangedLine(1, "secret") });
        FakeGitleaksProcessRunner runner = Reporting(SINGLE_FINDING_REPORT);
        GitleaksAdapter adapter = CreateAdapter(runner);

        Func<Task> act = async () => await TestSupport.CollectAsync(adapter, change);

        await act.Should().ThrowAsync<DetectionException>().WithMessage("*路径非法*");
        runner.CallCount.Should().Be(0, "路径逃逸必须在写入 / 启动进程之前拒绝");
    }

    [Fact]
    public async Task DetectAsync_EmptyChangedLines_DoesNotStartProcess()
    {
        var change = new FileChange("src/empty.txt", Array.Empty<ChangedLine>());
        FakeGitleaksProcessRunner runner = Reporting(SINGLE_FINDING_REPORT);
        GitleaksAdapter adapter = CreateAdapter(runner);

        List<Finding> findings = await TestSupport.CollectAsync(adapter, change);

        findings.Should().BeEmpty();
        runner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DetectAsync_ReportLineOutsideStagedRange_IsSkipped()
    {
        const string report = """
            [
              {
                "RuleID": "aws-access-token",
                "StartLine": 9,
                "Secret": "glk-test-secret-value-0001"
              }
            ]
            """;
        FakeGitleaksProcessRunner runner = Reporting(report);
        GitleaksAdapter adapter = CreateAdapter(runner);

        List<Finding> findings = await TestSupport.CollectAsync(adapter, CreateSingleLineChange());

        findings.Should().BeEmpty("报告行号越界说明暂存副本与报告不一致，不能映射到错误行号");
    }

    private static FileChange CreateSingleLineChange() =>
        new FileChange("src/app.env", new[] { new ChangedLine(1, "aws_key = " + RAW_SECRET) });

    private static GitleaksAdapter CreateAdapter(
        FakeGitleaksProcessRunner runner,
        IReadOnlyCollection<string>? disabledRuleIds = null) =>
        new GitleaksAdapter(
            new GitleaksAdapterOptions
            {
                ExecutablePath = "gitleaks",
                DisabledRuleIds = disabledRuleIds ?? Array.Empty<string>(),
            },
            TestSupport.CreateFactory(),
            runner);

    private static FakeGitleaksProcessRunner Reporting(string reportJson, int exitCode = 1) =>
        new FakeGitleaksProcessRunner(
            (_, reportPath) =>
            {
                File.WriteAllText(reportPath, reportJson);
                return new GitleaksProcessResult(exitCode, string.Empty, string.Empty);
            });

    /// <summary>记录调用参数、暂存文件内容并按委托生成报告文件的子进程替身。</summary>
    private sealed class FakeGitleaksProcessRunner : IGitleaksProcessRunner
    {
        private readonly Func<string, string, GitleaksProcessResult> _behavior;

        internal FakeGitleaksProcessRunner(Func<string, string, GitleaksProcessResult> behavior) =>
            _behavior = behavior;

        internal int CallCount { get; private set; }

        internal string? SourceDirectory { get; private set; }

        internal string? ReportPath { get; private set; }

        internal IReadOnlyList<string> LastArguments { get; private set; } = Array.Empty<string>();

        internal Dictionary<string, string> StagedFiles { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public Task<GitleaksProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastArguments = arguments;
            SourceDirectory = GetArgument(arguments, "--source");
            ReportPath = GetArgument(arguments, "--report-path");

            foreach (string file in Directory.EnumerateFiles(SourceDirectory!, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(SourceDirectory!, file).Replace('\\', '/');
                StagedFiles[relativePath] = File.ReadAllText(file);
            }

            return Task.FromResult(_behavior(SourceDirectory!, ReportPath!));
        }

        private static string? GetArgument(IReadOnlyList<string> arguments, string name)
        {
            for (int i = 0; i < arguments.Count - 1; i++)
            {
                if (arguments[i] == name)
                {
                    return arguments[i + 1];
                }
            }

            return null;
        }
    }
}
