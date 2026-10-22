using FluentAssertions;
using GitGuard.Core;

namespace GitGuard.Cli.Tests;

/// <summary>
/// 异常 → 退出码 / 错误码映射单元测试（<c>docs/10-异常处理设计.md</c> §3 / §4）：
/// 每个已登记异常类型必须落在文档承诺的退出码上，未登记类型一律 99（FailClosed）。
/// </summary>
public sealed class ExitCodeMapperTests
{
    [Fact]
    public void Map_GitException_ClassifiesRepositoryAndCommandFailures()
    {
        ExitCodeMapper.Map(new GitException(GitException.NOT_A_GIT_REPOSITORY, "不在仓库内"))
            .Should().Be(ExitCodes.NOT_A_GIT_REPOSITORY);
        ExitCodeMapper.Map(new GitException(GitException.GIT_COMMAND_FAILED, "git 失败"))
            .Should().Be(ExitCodes.GIT_COMMAND_FAILED);
    }

    [Fact]
    public void Map_HookException_ClassifiesPermissionUnknownAndLockFailures()
    {
        ExitCodeMapper.Map(new HookException(HookException.WRITE_PERMISSION_DENIED, "只读"))
            .Should().Be(ExitCodes.WRITE_PERMISSION_DENIED);
        ExitCodeMapper.Map(new HookException(HookException.UNKNOWN_HOOK_PRESENT, "二进制 Hook"))
            .Should().Be(ExitCodes.UNKNOWN_HOOK_PRESENT);
        ExitCodeMapper.Map(new HookException(HookException.INSTALL_IN_PROGRESS, "已有安装进程"))
            .Should().Be(ExitCodes.INSTALL_IN_PROGRESS);
    }

    [Fact]
    public void Map_UnregisteredHookErrorCode_FallsBackToUnknownError()
    {
        ExitCodeMapper.Map(new HookException("SOMETHING_NEW", "未登记")).Should().Be(ExitCodes.UNKNOWN_ERROR);
    }

    [Fact]
    public void Map_AllowlistException_ClassifiesValidationAndNotFound()
    {
        ExitCodeMapper.Map(new AllowlistException(AllowlistException.ALLOWLIST_VALIDATION_FAILED, "参数非法"))
            .Should().Be(ExitCodes.ALLOWLIST_VALIDATION_FAILED);
        ExitCodeMapper.Map(new AllowlistException(AllowlistException.ALLOWLIST_NOT_FOUND, "条目不存在"))
            .Should().Be(ExitCodes.ALLOWLIST_NOT_FOUND);
        ExitCodeMapper.Map(new AllowlistException("SOMETHING_NEW", "未登记"))
            .Should().Be(ExitCodes.ALLOWLIST_NOT_FOUND);
    }

    [Fact]
    public void Map_OtherRegisteredExceptions_FollowExitCodeTable()
    {
        ExitCodeMapper.Map(new ConfigurationException("坏 YAML")).Should().Be(ExitCodes.CONFIG_INVALID);
        ExitCodeMapper.Map(new DetectionException("检测器异常")).Should().Be(ExitCodes.DETECTOR_FAILED);
        ExitCodeMapper.Map(new SqliteException("审计库打开失败")).Should().Be(ExitCodes.AUDIT_STORE_FAILED);
        ExitCodeMapper.Map(new AuditExportException("导出写入失败")).Should().Be(ExitCodes.AUDIT_EXPORT_FAILED);
        ExitCodeMapper.Map(new OperationCanceledException()).Should().Be(ExitCodes.SCAN_TIMEOUT);
        ExitCodeMapper.Map(new InvalidOperationException("未分类")).Should().Be(ExitCodes.UNKNOWN_ERROR);
    }

    [Fact]
    public void ErrorCodeOf_ReturnsRegisteredErrorCodes()
    {
        ExitCodeMapper.ErrorCodeOf(new GitException(GitException.NOT_A_GIT_REPOSITORY, "不在仓库内"))
            .Should().Be(GitException.NOT_A_GIT_REPOSITORY);
        ExitCodeMapper.ErrorCodeOf(new HookException(HookException.WRITE_PERMISSION_DENIED, "只读"))
            .Should().Be(HookException.WRITE_PERMISSION_DENIED);
        ExitCodeMapper.ErrorCodeOf(new AllowlistException(AllowlistException.ALLOWLIST_VALIDATION_FAILED, "参数非法"))
            .Should().Be(AllowlistException.ALLOWLIST_VALIDATION_FAILED);
        ExitCodeMapper.ErrorCodeOf(new ConfigurationException("坏 YAML"))
            .Should().Be(ConfigurationException.CONFIG_INVALID);
        ExitCodeMapper.ErrorCodeOf(new DetectionException("检测器异常"))
            .Should().Be(DetectionException.DETECTOR_FAILED);
        ExitCodeMapper.ErrorCodeOf(new SqliteException("审计库打开失败"))
            .Should().Be(SqliteException.AUDIT_STORE_FAILED);
        ExitCodeMapper.ErrorCodeOf(new AuditExportException("导出写入失败"))
            .Should().Be(AuditExportException.AUDIT_EXPORT_FAILED);
        ExitCodeMapper.ErrorCodeOf(new OperationCanceledException())
            .Should().Be(ExitCodeMapper.SCAN_TIMEOUT);
        ExitCodeMapper.ErrorCodeOf(new InvalidOperationException("未分类"))
            .Should().Be(ExitCodeMapper.UNKNOWN_ERROR);
    }

    [Fact]
    public void Map_NullException_Throws()
    {
        FluentActions.Invoking(() => ExitCodeMapper.Map(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ExitCodeMapper.ErrorCodeOf(null!)).Should().Throw<ArgumentNullException>();
    }
}
