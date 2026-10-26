using System.Xml.Linq;
using FluentAssertions;

namespace GitGuard.Desktop.Tests;

/// <summary>
/// 仓库页布局回归测试（GGD-0057）：详情操作区固定于底部并使用换行布局，
/// 防止后续改回单行 StackPanel 后在默认 / 最小窗口宽度下裁掉右侧按钮。
/// </summary>
public sealed class RepositoriesViewLayoutTests
{
    private static readonly XNamespace _avalonia = "https://github.com/avaloniaui";

    /// <summary>操作区位于详情网格第二行，六个功能按钮由 WrapPanel 承载。</summary>
    [Fact]
    public void DetailActionBar_IsPinnedAndWrapsAllButtons()
    {
        string root = ResolveRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "GitGuard.Desktop", "Views", "RepositoriesView.axaml"));

        XElement wrapPanel = document.Descendants(_avalonia + "WrapPanel").Single();
        XElement actionPanel = wrapPanel.Parent!;

        actionPanel.Name.Should().Be(_avalonia + "StackPanel");
        actionPanel.Attribute("Grid.Row")?.Value.Should().Be("1", "操作区应固定在详情底部，不随详情内容滚出窗口");
        wrapPanel.Attribute("Orientation")?.Value.Should().Be("Horizontal");
        wrapPanel.Elements(_avalonia + "Button").Should().HaveCount(6, "仓库详情需保留扫描 / 历史 / 安装 / 卸载 / 白名单 / 移除六个操作");
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (true)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GitGuard.sln")))
            {
                return directory.FullName;
            }

            if (directory.Parent is not { } parent)
            {
                break;
            }

            directory = parent;
        }

        throw new InvalidOperationException("未能从测试输出目录定位 GitGuard.sln");
    }
}
