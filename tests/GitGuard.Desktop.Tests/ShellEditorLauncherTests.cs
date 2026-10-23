using FluentAssertions;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.Tests;

/// <summary>
/// 外部编辑器启动器测试（GGD-0043）：只覆盖不启动进程的校验分支（仓库 / 文件缺失、路径逃逸、非法行号），
/// 避免测试环境真实拉起编辑器。
/// </summary>
public sealed class ShellEditorLauncherTests
{
    [Fact]
    public async Task OpenAsync_MissingRepository_ReturnsRepositoryMissing()
    {
        var launcher = new ShellEditorLauncher();
        string missing = Path.Combine(Path.GetTempPath(), "gitguard-editor-missing-" + Guid.NewGuid().ToString("N"));

        EditorLaunchResult result = await launcher.OpenAsync(
            missing,
            "src/Program.cs",
            1,
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(EditorLaunchStatus.RepositoryMissing);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAsync_MissingFile_ReturnsFileMissing()
    {
        string repositoryPath = CreateDirectory();
        try
        {
            var launcher = new ShellEditorLauncher();

            EditorLaunchResult result = await launcher.OpenAsync(
                repositoryPath,
                "src/Missing.cs",
                10,
                TestContext.Current.CancellationToken);

            result.Status.Should().Be(EditorLaunchStatus.FileMissing);
            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsync_RelativePathOutsideRepository_IsRefused()
    {
        string parent = CreateDirectory();
        string repositoryPath = Path.Combine(parent, "repo");
        Directory.CreateDirectory(repositoryPath);
        string outsideFile = Path.Combine(parent, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "canary", TestContext.Current.CancellationToken);
        try
        {
            var launcher = new ShellEditorLauncher();

            EditorLaunchResult result = await launcher.OpenAsync(
                repositoryPath,
                Path.Combine("..", "outside.txt"),
                3,
                TestContext.Current.CancellationToken);

            result.Status.Should().Be(EditorLaunchStatus.PathOutsideRepository);
            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsync_LineNumberBelowOne_Throws()
    {
        string repositoryPath = CreateDirectory();
        try
        {
            var launcher = new ShellEditorLauncher();

            Func<Task> act = () => launcher.OpenAsync(
                repositoryPath,
                "src/Program.cs",
                0,
                TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }
        finally
        {
            Directory.Delete(repositoryPath, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "gitguard-editor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
