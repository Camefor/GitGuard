using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>
/// 一次文件权限修复的记录（08-权限设计 §5 / §7），用于生成 <c>FilePermissionFixed</c> 审计事件。
/// </summary>
public sealed record FilePermissionFix
{
    /// <summary>初始化权限修复记录。</summary>
    /// <param name="filePath">被修复的文件或目录路径。</param>
    /// <param name="previousMode">修复前的权限表示：Unix 为八进制 mode（如 <c>0644</c>），Windows 为 SDDL。</param>
    /// <param name="newMode">修复后的权限表示：Unix 为八进制 mode（如 <c>0600</c>），Windows 为 SDDL。</param>
    /// <exception cref="ArgumentException">任一参数为空或全空白。</exception>
    public FilePermissionFix(string filePath, string previousMode, string newMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousMode);
        ArgumentException.ThrowIfNullOrWhiteSpace(newMode);

        FilePath = filePath;
        PreviousMode = previousMode;
        NewMode = newMode;
    }

    /// <summary>被修复的文件或目录路径。</summary>
    public string FilePath { get; }

    /// <summary>修复前的权限表示（Unix 八进制 mode / Windows SDDL）。</summary>
    public string PreviousMode { get; }

    /// <summary>修复后的权限表示（Unix 八进制 mode / Windows SDDL）。</summary>
    public string NewMode { get; }
}

/// <summary>
/// 本地数据文件权限加固（08-权限设计 §5）：
/// <list type="bullet">
///   <item><description>Unix：目录收紧为 <c>0700</c>、文件收紧为 <c>0600</c>，并返回修复前后的 mode。</description></item>
///   <item><description>Windows：检测 ACL 中 <c>Everyone</c>（S-1-1-0）/ <c>Authenticated Users</c>（S-1-5-11）的 Allow 规则；
///   命中则禁用继承并重置为仅当前用户 FullControl（08 §5.2）。</description></item>
/// </list>
/// 返回 <see langword="null"/> 表示路径不存在或权限已符合要求，无需写审计事件。
/// </summary>
public static class FilePermissionHardener
{
    /// <summary>Unix 文件期望 mode：<c>0600</c>。</summary>
    internal const UnixFileMode OWNER_ONLY_FILE_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Unix 目录期望 mode：<c>0700</c>。</summary>
    internal const UnixFileMode OWNER_ONLY_DIRECTORY_MODE = OWNER_ONLY_FILE_MODE | UnixFileMode.UserExecute;

    /// <summary>加固单个文件（Unix 0600；Windows 移除宽泛 Allow 规则）。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>发生修复时返回修复记录；路径不存在或权限已合规时返回 <see langword="null"/>。</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空或全空白。</exception>
    /// <exception cref="IOException">读取或写入权限失败。</exception>
    /// <exception cref="UnauthorizedAccessException">当前用户无权修改目标权限。</exception>
    public static FilePermissionFix? HardenFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Harden(path, isDirectory: false);
    }

    /// <summary>加固单个目录（Unix 0700；Windows 移除宽泛 Allow 规则）。</summary>
    /// <param name="path">目录路径。</param>
    /// <returns>发生修复时返回修复记录；路径不存在或权限已合规时返回 <see langword="null"/>。</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空或全空白。</exception>
    /// <exception cref="IOException">读取或写入权限失败。</exception>
    /// <exception cref="UnauthorizedAccessException">当前用户无权修改目标权限。</exception>
    public static FilePermissionFix? HardenDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Harden(path, isDirectory: true);
    }

    private static FilePermissionFix? Harden(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsAclHardener.Harden(path, isDirectory);
        }

        bool exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
        if (!exists)
        {
            return null;
        }

        UnixFileMode desired = isDirectory ? OWNER_ONLY_DIRECTORY_MODE : OWNER_ONLY_FILE_MODE;
        UnixFileMode current = File.GetUnixFileMode(path);
        if (current == desired)
        {
            return null;
        }

        File.SetUnixFileMode(path, desired);
        return new FilePermissionFix(path, FormatUnixMode(current), FormatUnixMode(desired));
    }

    /// <summary>把 Unix mode 格式化为四位八进制字符串（如 <c>0600</c>），仅保留权限位。</summary>
    private static string FormatUnixMode(UnixFileMode mode) =>
        "0" + Convert.ToString((int)mode & 0x1FF, 8).PadLeft(3, '0');

    /// <summary>Windows ACL 加固（08-权限设计 §5.2）；平台注解隔离保证 CA1416 只在 Windows 目标上分析。</summary>
    [SupportedOSPlatform("windows")]
    private static class WindowsAclHardener
    {
        /// <summary>Everyone 的良定义 SID。</summary>
        private const string EVERYONE_SID = "S-1-1-0";

        /// <summary>Authenticated Users 的良定义 SID。</summary>
        private const string AUTHENTICATED_USERS_SID = "S-1-5-11";

        internal static FilePermissionFix? Harden(string path, bool isDirectory)
        {
            FileSystemSecurity security;
            DirectoryInfo? directory = null;
            FileInfo? file = null;
            if (isDirectory)
            {
                directory = new DirectoryInfo(path);
                if (!directory.Exists)
                {
                    return null;
                }

                security = directory.GetAccessControl(AccessControlSections.Access);
            }
            else
            {
                file = new FileInfo(path);
                if (!file.Exists)
                {
                    return null;
                }

                security = file.GetAccessControl(AccessControlSections.Access);
            }

            string previousMode = security.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            if (!HasBroadAllowRule(security))
            {
                return null;
            }

            SecurityIdentifier currentUser = ResolveCurrentUserSid();

            // 禁用继承以剔除父目录（如 %LOCALAPPDATA%）带入的 Everyone / Authenticated Users 规则，
            // 随后清空显式规则并仅保留当前用户的完全控制（08-权限设计 §5.2「重置为仅 CurrentUser」）
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            RemoveExplicitRules(security);
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            if (directory is not null)
            {
                directory.SetAccessControl((DirectorySecurity)security);
            }
            else if (file is not null)
            {
                file.SetAccessControl((FileSecurity)security);
            }
            else
            {
                throw new InvalidOperationException("Windows ACL 加固目标既不是已存在的文件也不是目录。");
            }

            string newMode = security.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            return new FilePermissionFix(path, previousMode, newMode);
        }

        private static bool HasBroadAllowRule(FileSystemSecurity security)
        {
            foreach (FileSystemAccessRule rule in security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow
                    && rule.IdentityReference is SecurityIdentifier sid
                    && (string.Equals(sid.Value, EVERYONE_SID, StringComparison.Ordinal)
                        || string.Equals(sid.Value, AUTHENTICATED_USERS_SID, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveExplicitRules(FileSystemSecurity security)
        {
            foreach (FileSystemAccessRule rule in security.GetAccessRules(
                includeExplicit: true,
                includeInherited: false,
                targetType: typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRule(rule);
            }
        }

        private static SecurityIdentifier ResolveCurrentUserSid()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User
                ?? throw new InvalidOperationException("无法解析当前 Windows 用户 SID，拒绝保留宽泛 ACL（08-权限设计 §5.2）。");
        }
    }
}
