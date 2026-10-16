using System.Text;

namespace GitGuard.Cli;

/// <summary>
/// 进程入口（<c>模块设计/Cli.md</c> §6.1）：仅做控制台编码准备并转发 <see cref="CliApplication.RunAsync"/>，
/// 业务与退出码映射一概不在此处实现。
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        TryEnableUtf8Console();
        return await CliApplication.RunAsync(args, CliHost.CreateDefault()).ConfigureAwait(false);
    }

    /// <summary>把控制台输出编码切到 UTF-8（中文报告在 Windows 传统代码页下不出现乱码）。</summary>
    private static void TryEnableUtf8Console()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // 无控制台 / 句柄重定向：保持默认编码，不影响管道输出
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
