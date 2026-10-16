using System.Text;
using GitGuard.Core;

namespace GitGuard.Application.Hooks;

/// <summary>目标 Hook 文件的探测状态（分发脚本识别与接管决策的输入）。</summary>
internal enum HookFileState
{
    /// <summary>目标不存在。</summary>
    Missing = 0,

    /// <summary>GitGuard 托管脚本（首两行含 <c># gitguard-managed v</c> 标记）。</summary>
    ManagedScript = 1,

    /// <summary>非托管的普通文本脚本：可安全备份后包裹（TC-FR-010-02 / DR-05）。</summary>
    LegacyScript = 2,

    /// <summary>二进制文件（头部含 NUL 字节）：需 <c>--force</c> 才可接管（SRS FR-010 异常处理）。</summary>
    Binary = 3,

    /// <summary>符号链接：写入会穿透链接，需 <c>--force</c> 才可接管（08-权限设计 §9）。</summary>
    SymbolicLink = 4,
}

/// <summary>
/// Hook 文件系统操作（Application 层直接使用 System.IO，与扫描用例一致不引入文件系统抽象）。
/// 所有 IO 失败统一包装为 <see cref="HookException"/>(<c>WRITE_PERMISSION_DENIED</c> → 退出码 3)。
/// </summary>
internal static class HookFileOperations
{
    private const int HEAD_BYTES = 8192;
    private const string MANAGED_MARKER = "# gitguard-managed v";

    /// <summary>探测目标 Hook 文件状态（一次头读完成托管标记与二进制判定）。</summary>
    /// <param name="hookPath">目标 Hook 绝对路径。</param>
    /// <returns>文件状态（<see cref="HookFileState.Missing"/> / <see cref="HookFileState.ManagedScript"/> 等）。</returns>
    /// <exception cref="HookException">读取失败（权限不足等）。</exception>
    internal static HookFileState Inspect(string hookPath)
    {
        if (IsSymbolicLink(hookPath))
        {
            return HookFileState.SymbolicLink;
        }

        if (!File.Exists(hookPath))
        {
            return HookFileState.Missing;
        }

        byte[] head = ReadHead(hookPath);
        if (Array.IndexOf(head, (byte)0) >= 0)
        {
            return HookFileState.Binary;
        }

        string headText = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(head);
        return ContainsManagedMarker(headText) ? HookFileState.ManagedScript : HookFileState.LegacyScript;
    }

    /// <summary>创建 hooks 目录（不存在时）。</summary>
    /// <param name="directory">hooks 目录绝对路径。</param>
    /// <exception cref="HookException">创建失败。</exception>
    internal static void EnsureDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateWriteDenied(directory, ex);
        }
    }

    /// <summary>把原 Hook 复制为备份文件（复制而非改名：目标随后被分发脚本替换，08-权限设计 §9 防符号链接攻击）。</summary>
    /// <param name="hookPath">原 Hook 绝对路径。</param>
    /// <param name="backupPath">备份文件绝对路径（调用方保证此前不存在）。</param>
    /// <exception cref="HookException">复制失败（含权限不足、符号链接悬空）。</exception>
    internal static void CreateBackup(string hookPath, string backupPath)
    {
        try
        {
            File.Copy(hookPath, backupPath, overwrite: false);

            // 复制不保证保留 Unix 可执行位，而分发脚本以 [ -x ] 判定是否调用备份（TC-DR-05-01）：显式对齐源文件权限
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(backupPath, File.GetUnixFileMode(hookPath));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateWriteDenied(backupPath, ex);
        }
    }

    /// <summary>删除 Hook 文件（分发脚本或待接管目标）；文件不存在时不抛错。</summary>
    /// <param name="hookPath">目标绝对路径。</param>
    /// <exception cref="HookException">删除失败。</exception>
    internal static void DeleteFile(string hookPath)
    {
        try
        {
            File.Delete(hookPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateWriteDenied(hookPath, ex);
        }
    }

    /// <summary>把备份文件改名为目标 Hook（卸载还原，FR-011）；目标已存在时先删除。</summary>
    /// <param name="backupPath">备份文件绝对路径。</param>
    /// <param name="hookPath">目标 Hook 绝对路径。</param>
    /// <exception cref="HookException">还原失败。</exception>
    internal static void RestoreBackup(string backupPath, string hookPath)
    {
        DeleteFile(hookPath);
        try
        {
            File.Move(backupPath, hookPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateWriteDenied(hookPath, ex);
        }
    }

    /// <summary>写入分发脚本（UTF-8 无 BOM）；Unix 下设置 0755 可执行权限（08-权限设计 §5.1）。</summary>
    /// <param name="hookPath">目标绝对路径。</param>
    /// <param name="script">脚本全文（LF 行尾）。</param>
    /// <exception cref="HookException">写入失败。</exception>
    internal static void WriteScript(string hookPath, string script)
    {
        try
        {
            File.WriteAllText(hookPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // 平台守卫置于本方法内：CA1416 不做跨方法分析（同 SqliteAuditStore 的既有先例）
            if (!OperatingSystem.IsWindows())
            {
                // 0755：与 08-权限设计 §5.1 的 Hook 文件权限要求一致
                UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
                File.SetUnixFileMode(hookPath, mode);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateWriteDenied(hookPath, ex);
        }
    }

    private static byte[] ReadHead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int length = (int)Math.Min(HEAD_BYTES, stream.Length);
            byte[] head = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(head, offset, length - offset);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            if (offset != length)
            {
                Array.Resize(ref head, offset);
            }

            return head;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateWriteDenied(path, ex);
        }
    }

    private static bool ContainsManagedMarker(string headText)
    {
        string[] lines = headText.Split('\n');
        int lineCount = Math.Min(lines.Length, 2);
        for (int i = 0; i < lineCount; i++)
        {
            if (lines[i].Contains(MANAGED_MARKER, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // 符号链接判定用 File.Exists 前置（File.GetAttributes 对不存在的路径会抛 FileNotFoundException）；
    // 悬空符号链接按 Missing 处理属于已知局限（写入会穿透链接，用户可先手工清理）
    private static bool IsSymbolicLink(string path) =>
        File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static HookException CreateWriteDenied(string path, Exception inner) =>
        new HookException(
            HookException.WRITE_PERMISSION_DENIED,
            $"无法写入 Hook 相关文件（08-权限设计 §5）：{path}",
            inner);
}
