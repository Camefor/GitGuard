using FluentAssertions;
using GitGuard.Application.Configuration;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Tests;

/// <summary>
/// 配置加载器测试（TC-FR-070-01~04 / FR-071，<c>模块设计/Policy.md</c> §6.2 / §7）：
/// 三层优先级（内置默认 &lt; 用户级 &lt; 项目级）、<c>version: 1</c> 必填、未知版本与越界取值 FailClosed（13）、
/// 宽松解析警告（未知键 / 暂未接线开关）。全部用例使用真实临时文件，不 mock 文件系统。
/// </summary>
public sealed class GitGuardConfigLoaderTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    // TC-FR-070-02：不存在配置文件 → 内置默认，加载链为空
    [Fact]
    public void Load_NoConfigFiles_UsesBuiltinDefaults()
    {
        using var project = new TempDirectory();

        GitGuardConfig config = CreateLoader(project.Root).Load();

        config.Version.Should().Be(GitGuardConfig.SUPPORTED_VERSION);
        config.Scan.MaxFileBytes.Should().Be(ScanSettings.DEFAULT_MAX_FILE_BYTES);
        config.Scan.Timeout.Should().Be(ScanSettings.DEFAULT_TIMEOUT);
        config.Secrets.Enabled.Should().BeTrue();
        config.Privacy.Enabled.Should().BeTrue();
        config.LocalPath.Enabled.Should().BeTrue();
        config.Url.Enabled.Should().BeTrue();
        config.LogVerbose.Should().BeFalse();
        config.Allowlist.Should().BeEmpty();
        config.LoadedConfigFiles.Should().BeEmpty();
        config.Warnings.Should().BeEmpty();
    }

    // TC-FR-070-03：version 缺失 → FailClosed（退出码 13 的异常来源）
    [Fact]
    public void Load_MissingVersion_ThrowsConfigurationException()
    {
        using var project = new TempDirectory();
        const string yaml = "scan:\n  max_file_bytes: 2048\n";
        File.WriteAllText(ProjectConfigPath(project), yaml);

        Action act = () => CreateLoader(project.Root).Load();

        act.Should().Throw<ConfigurationException>().Which.Message.Should().Contain("缺少 version");
    }

    // TC-FR-070-04：未知版本 → FailClosed
    [Fact]
    public void Load_UnsupportedVersion_ThrowsConfigurationException()
    {
        using var project = new TempDirectory();
        File.WriteAllText(ProjectConfigPath(project), "version: 99\n");

        Action act = () => CreateLoader(project.Root).Load();

        ConfigurationException exception = act.Should().Throw<ConfigurationException>().Subject.Single();
        exception.ErrorCode.Should().Be(ConfigurationException.CONFIG_INVALID);
        exception.SourcePath.Should().EndWith(".gitguard.yml");
        exception.Message.Should().Contain("不受支持");
    }

    [Fact]
    public void Load_InvalidYaml_ThrowsWithLineNumber()
    {
        using var project = new TempDirectory();
        File.WriteAllText(ProjectConfigPath(project), "version: 1\nscan: [unclosed\n");

        Action act = () => CreateLoader(project.Root).Load();

        ConfigurationException exception = act.Should().Throw<ConfigurationException>().Subject.Single();
        exception.Message.Should().Contain("YAML 语法错误");
        exception.Line.Should().BePositive();
    }

    // TC-FR-070-01：项目级覆盖用户级（标量覆盖、列表整体替换）
    [Fact]
    public void Load_ProjectOverridesUserSection()
    {
        using var project = new TempDirectory();
        string userDirectory = Path.Combine(project.Root, "user");
        Directory.CreateDirectory(userDirectory);
        string userConfigPath = Path.Combine(userDirectory, "config.yml");

        const string userYaml = """
            version: 1
            scan:
              max_file_bytes: 2048
              timeout_ms: 5000
            rules:
              secrets:
                min_entropy: 3.0
            allowlist:
              entries:
                - rule_id: GGD/SECRET/AWS-AKID
                  reason: 用户级条目（应被项目级整体替换）
            """;
        const string projectYaml = """
            version: 1
            scan:
              max_file_bytes: 8192
            allowlist:
              entries:
                - rule_id: GGD/PRIVACY/CN-ID
                  reason: 项目级条目
            """;
        File.WriteAllText(userConfigPath, userYaml);
        File.WriteAllText(ProjectConfigPath(project), projectYaml);

        GitGuardConfig config = CreateLoader(project.Root, userConfigPath).Load();

        config.Scan.MaxFileBytes.Should().Be(8192, "项目级覆盖用户级同名标量");
        config.Scan.Timeout.Should().Be(TimeSpan.FromMilliseconds(5000), "未覆盖的字段沿用用户级取值");
        config.Secrets.MinEntropy.Should().Be(3.0);
        config.Allowlist.Should().ContainSingle().Which.RuleId.Should().Be("GGD/PRIVACY/CN-ID");
        config.LoadedConfigFiles.Should().HaveCount(2);
        config.LoadedConfigFiles[0].Should().Be(Path.GetFullPath(userConfigPath), "加载链低优先级在前");
    }

    [Fact]
    public void Load_AllowlistEntry_CarriesReasonAndCreatedBy()
    {
        using var project = new TempDirectory();
        const string yaml = """
            version: 1
            allowlist:
              entries:
                - path_pattern: config/*.env
                  reason: 示例配置中的占位密钥
            """;
        File.WriteAllText(ProjectConfigPath(project), yaml);

        GitGuardConfig config = CreateLoader(project.Root).Load();

        AllowlistEntry entry = config.Allowlist.Should().ContainSingle().Subject;
        entry.PathPattern.Should().Be("config/*.env");
        entry.Reason.Should().Be("示例配置中的占位密钥");
        entry.CreatedAt.Should().Be(FixedNow);
        entry.CreatedBy.Should().Be("tester");
    }

    [Fact]
    public void Load_AllowlistEntry_WithShortReason_FailsClosed()
    {
        using var project = new TempDirectory();
        const string yaml = """
            version: 1
            allowlist:
              entries:
                - rule_id: GGD/SECRET/AWS-AKID
                  reason: 短
            """;
        File.WriteAllText(ProjectConfigPath(project), yaml);

        Action act = () => CreateLoader(project.Root).Load();

        act.Should().Throw<ConfigurationException>().Which.Message.Should().Contain("allowlist.entries[0]");
    }

    [Fact]
    public void Load_CriticalAllow_ViolatesHardFloor()
    {
        using var project = new TempDirectory();
        File.WriteAllText(ProjectConfigPath(project), "version: 1\npolicy:\n  critical: allow\n");

        Action act = () => CreateLoader(project.Root).Load();

        act.Should().Throw<ConfigurationException>().Which.Message.Should().Contain("policy 段落校验失败");
    }

    [Fact]
    public void Load_UnknownPolicyAction_FailsClosed()
    {
        using var project = new TempDirectory();
        File.WriteAllText(ProjectConfigPath(project), "version: 1\npolicy:\n  low: ignore\n");

        Action act = () => CreateLoader(project.Root).Load();

        act.Should().Throw<ConfigurationException>().Which.Message.Should().Contain("policy.low 取值非法");
    }

    [Fact]
    public void Load_UseGitleaks_IsRejectedInsteadOfSilentlyIgnored()
    {
        using var project = new TempDirectory();
        const string yaml = "version: 1\nrules:\n  secrets:\n    use_gitleaks: true\n";
        File.WriteAllText(ProjectConfigPath(project), yaml);

        Action act = () => CreateLoader(project.Root).Load();

        act.Should().Throw<ConfigurationException>().Which.Message.Should().Contain("use_gitleaks");
    }

    [Fact]
    public void Load_DeferredSwitches_ProduceWarningsWithoutBlocking()
    {
        using var project = new TempDirectory();
        const string yaml = """
            version: 1
            scan:
              pre_commit: false
              fail_closed: false
            logging:
              verbose: true
            exclude:
              paths:
                - "**/vendor/**"
            """;
        File.WriteAllText(ProjectConfigPath(project), yaml);

        GitGuardConfig config = CreateLoader(project.Root).Load();

        config.LogVerbose.Should().BeTrue();
        config.ExcludePaths.Should().ContainSingle().Which.Should().Be("**/vendor/**");
        config.Warnings.Should().Contain(warning => warning.Contains("scan.pre_commit"));
        config.Warnings.Should().Contain(warning => warning.Contains("fail_closed"));
        config.Warnings.Should().Contain(warning => warning.Contains("logging.verbose"));
        config.Warnings.Should().NotContain(warning => warning.Contains("exclude"));
    }

    [Fact]
    public void Load_ExcludeSection_MapsPathsAndFilesWithTrimming()
    {
        using var project = new TempDirectory();
        const string yaml = """
            version: 1
            exclude:
              paths:
                - " **/obj/** "
              files:
                - "*.min.js"
            """;
        File.WriteAllText(ProjectConfigPath(project), yaml);

        GitGuardConfig config = CreateLoader(project.Root).Load();

        config.ExcludePaths.Should().ContainSingle().Which.Should().Be("**/obj/**");
        config.ExcludeFiles.Should().ContainSingle().Which.Should().Be("*.min.js");
        config.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Load_ProjectExcludeOverridesUserExcludePerField()
    {
        using var project = new TempDirectory();
        string userConfigPath = Path.Combine(project.Root, "user-config.yml");
        const string userYaml = """
            version: 1
            exclude:
              paths:
                - "**/user/**"
              files:
                - "*.user"
            """;
        const string projectYaml = """
            version: 1
            exclude:
              paths:
                - "**/project/**"
            """;
        File.WriteAllText(userConfigPath, userYaml);
        File.WriteAllText(ProjectConfigPath(project), projectYaml);

        GitGuardConfig config = CreateLoader(project.Root, userConfigPath).Load();

        config.ExcludePaths.Should().ContainSingle().Which.Should().Be("**/project/**");
        config.ExcludeFiles.Should().ContainSingle().Which.Should().Be("*.user");
    }

    [Fact]
    public void Load_ExcludeBlankItem_FailsClosed()
    {
        using var project = new TempDirectory();
        const string yaml = "version: 1\nexclude:\n  files:\n    - \"  \"\n";
        File.WriteAllText(ProjectConfigPath(project), yaml);

        Action act = () => CreateLoader(project.Root).Load();

        act.Should().Throw<ConfigurationException>().Which.Message.Should().Contain("exclude.files");
    }

    [Fact]
    public void Load_UnknownExcludeKey_ProducesWarning()
    {
        using var project = new TempDirectory();
        const string yaml = "version: 1\nexclude:\n  path:\n    - \"**/bin/**\"\n";
        File.WriteAllText(ProjectConfigPath(project), yaml);

        GitGuardConfig config = CreateLoader(project.Root).Load();

        config.ExcludePaths.Should().BeEmpty();
        config.Warnings.Should().Contain(warning => warning.Contains("exclude.path"));
    }

    [Fact]
    public void Load_UnknownTopLevelKey_ProducesWarning()
    {
        using var project = new TempDirectory();
        File.WriteAllText(ProjectConfigPath(project), "version: 1\ntypo_section:\n  enabled: true\n");

        GitGuardConfig config = CreateLoader(project.Root).Load();

        config.Warnings.Should().Contain(warning => warning.Contains("typo_section"));
    }

    [Fact]
    public void Load_LogFileSection_MapsSettings()
    {
        using var project = new TempDirectory();
        const string yaml = """
            version: 1
            logging:
              file:
                enabled: true
                path: ~/logs/gitguard.log
                max_bytes: 1024
                max_files: 2
            """;
        File.WriteAllText(ProjectConfigPath(project), yaml);

        GitGuardConfig config = CreateLoader(project.Root).Load();

        config.LogFile.Enabled.Should().BeTrue();
        config.LogFile.Path.Should().Be("~/logs/gitguard.log");
        config.LogFile.MaxBytes.Should().Be(1024);
        config.LogFile.MaxFiles.Should().Be(2);
    }

    [Fact]
    public void Load_LogFileWithoutPath_UsesInjectedPlatformDefault()
    {
        using var project = new TempDirectory();
        const string yaml = """
            version: 1
            logging:
              file:
                enabled: true
            """;
        File.WriteAllText(ProjectConfigPath(project), yaml);
        string defaultPath = Path.Combine(project.Root, "data", "logs", "gitguard.log");

        GitGuardConfig config = CreateLoader(project.Root, defaultLogFilePath: defaultPath).Load();

        config.LogFile.Enabled.Should().BeTrue();
        config.LogFile.Path.Should().Be(defaultPath, "缺省 path 必须跟随注入的平台数据目录，而不是固定 XDG 路径");
    }

    [Fact]
    public void Load_NoConfigFiles_UsesInjectedDefaultLogPath()
    {
        using var project = new TempDirectory();
        string defaultPath = Path.Combine(project.Root, "data", "logs", "gitguard.log");

        GitGuardConfig config = CreateLoader(project.Root, defaultLogFilePath: defaultPath).Load();

        config.LogFile.Enabled.Should().BeFalse();
        config.LogFile.Path.Should().Be(defaultPath);
    }

    [Theory]
    [InlineData("scan:\n  max_file_bytes: 0\n", "scan.max_file_bytes")]
    [InlineData("scan:\n  timeout_ms: 0\n", "scan.timeout_ms")]
    [InlineData("logging:\n  file:\n    max_bytes: -1\n", "logging.file.max_bytes")]
    [InlineData("logging:\n  file:\n    max_files: 0\n", "logging.file.max_files")]
    [InlineData("rules:\n  secrets:\n    min_entropy: -0.5\n", "rules.secrets.min_entropy")]
    public void Load_OutOfRangeValues_FailClosed(string section, string expectedKey)
    {
        using var project = new TempDirectory();
        File.WriteAllText(ProjectConfigPath(project), "version: 1\n" + section);

        Action act = () => CreateLoader(project.Root).Load();

        act.Should().Throw<ConfigurationException>().Which.Message.Should().Contain(expectedKey);
    }

    [Fact]
    public void Load_DisabledRules_AreTrimmed()
    {
        using var project = new TempDirectory();
        const string yaml = """
            version: 1
            rules:
              url:
                disabled_rules:
                  - " GGD/URL/GITHUB-PAT "
            """;
        File.WriteAllText(ProjectConfigPath(project), yaml);

        GitGuardConfig config = CreateLoader(project.Root).Load();

        config.Url.DisabledRules.Should().ContainSingle().Which.Should().Be("GGD/URL/GITHUB-PAT");
    }

    [Fact]
    public void Load_BlankListItems_FailClosed()
    {
        using var project = new TempDirectory();
        const string yaml = "version: 1\nrules:\n  url:\n    disabled_rules:\n      - \"\"\n";
        File.WriteAllText(ProjectConfigPath(project), yaml);

        Action act = () => CreateLoader(project.Root).Load();

        act.Should().Throw<ConfigurationException>().Which.Message.Should().Contain("rules.*.disabled_rules");
    }

    private static string ProjectConfigPath(TempDirectory project) =>
        Path.Combine(project.Root, GitGuardConfigLoader.PROJECT_CONFIG_FILE_NAME);

    private static GitGuardConfigLoader CreateLoader(
        string projectDirectory,
        string? userConfigPath = null,
        string? defaultLogFilePath = null) =>
        new GitGuardConfigLoader(
            projectDirectory,
            userConfigPath,
            new FixedClock(FixedNow),
            createdBy: "tester",
            defaultLogFilePath: defaultLogFilePath);

    /// <summary>真实临时目录夹具（配置加载全部经真实文件系统）。</summary>
    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "gitguard-config-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // 句柄偶发被系统占用：清理失败不影响测试结论
            }
        }
    }
}
