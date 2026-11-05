using System.Text.Json;
using FluentAssertions;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Model;

namespace GitGuard.Application.Tests;

/// <summary>
/// 基线增量登记用例测试（GGD-0062）：新建写入、幂等、保留既有条目、参数校验与损坏文件 FailClosed。
/// 只走 <see cref="BaselineAddUseCase"/> 公共入口，不直接触碰内部 <see cref="BaselineFile"/>。
/// </summary>
public sealed class BaselineAddUseCaseTests : IDisposable
{
    private const string RULE_ID = "GGD/SECRET/AWS-AKID";
    private const string FILE_PATH = "src/appsettings.json";

    private readonly string _repositoryRoot;
    private readonly BaselineAddUseCase _useCase;

    public BaselineAddUseCaseTests()
    {
        _repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            "gitguard-baseline-add-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repositoryRoot);
        _useCase = new BaselineAddUseCase(_repositoryRoot);
    }

    private string DefaultBaselinePath => Path.Combine(_repositoryRoot, ".gitguard-baseline.json");

    [Fact]
    public void Execute_NewEntry_WritesBaselineFileWithSchemaAndFingerprint()
    {
        string fingerprint = Fingerprint.FromHex(new string('a', 64)).Hex;

        BaselineAddResult result = _useCase.Execute(fingerprint, RULE_ID, FILE_PATH);

        result.AddedEntries.Should().Be(1);
        result.Path.Should().Be(DefaultBaselinePath);
        File.Exists(DefaultBaselinePath).Should().BeTrue();

        string json = File.ReadAllText(DefaultBaselinePath);
        json.Should().Contain("\"schema\": \"gitguard.baseline.v1\"");
        json.Should().Contain(fingerprint);
    }

    [Fact]
    public void Execute_ExistingFingerprint_IsNoOp()
    {
        string fingerprint = Fingerprint.FromHex(new string('b', 64)).Hex;
        _useCase.Execute(fingerprint, RULE_ID, FILE_PATH);

        BaselineAddResult result = _useCase.Execute(fingerprint, RULE_ID, FILE_PATH);

        result.AddedEntries.Should().Be(0);
        CountEntries(DefaultBaselinePath).Should().Be(1);
    }

    [Fact]
    public void Execute_SecondFingerprint_PreservesExistingEntries()
    {
        string first = Fingerprint.FromHex(new string('a', 64)).Hex;
        string second = Fingerprint.FromHex(new string('b', 64)).Hex;
        _useCase.Execute(first, RULE_ID, FILE_PATH);

        _useCase.Execute(second, RULE_ID, FILE_PATH);

        CountEntries(DefaultBaselinePath).Should().Be(2);
        string json = File.ReadAllText(DefaultBaselinePath);
        json.Should().Contain(first);
        json.Should().Contain(second);
    }

    [Fact]
    public void Execute_InvalidFingerprint_ThrowsBaselineInvalid()
    {
        Action act = () => _useCase.Execute("not-a-fingerprint", RULE_ID, FILE_PATH);

        act.Should().Throw<BaselineException>()
            .Which.ErrorCode.Should().Be(BaselineException.BASELINE_INVALID);
    }

    [Fact]
    public void Execute_InvalidRuleId_ThrowsBaselineInvalid()
    {
        string fingerprint = Fingerprint.FromHex(new string('a', 64)).Hex;

        Action act = () => _useCase.Execute(fingerprint, "AWS_KEY", FILE_PATH);

        act.Should().Throw<BaselineException>()
            .Which.ErrorCode.Should().Be(BaselineException.BASELINE_INVALID);
    }

    [Fact]
    public void Execute_CorruptBaselineFile_ThrowsBaselineInvalid()
    {
        string fingerprint = Fingerprint.FromHex(new string('a', 64)).Hex;
        File.WriteAllText(DefaultBaselinePath, "{ not valid json");

        Action act = () => _useCase.Execute(fingerprint, RULE_ID, FILE_PATH);

        act.Should().Throw<BaselineException>()
            .Which.ErrorCode.Should().Be(BaselineException.BASELINE_INVALID);
    }

    [Fact]
    public void Constructor_WhitespaceRepositoryRoot_ThrowsArgumentException()
    {
        Action act = () => _ = new BaselineAddUseCase("   ");

        act.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_repositoryRoot))
            {
                Directory.Delete(_repositoryRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // 测试隔离目录清理失败不影响断言
        }
    }

    private static int CountEntries(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("entries").GetArrayLength();
    }
}
