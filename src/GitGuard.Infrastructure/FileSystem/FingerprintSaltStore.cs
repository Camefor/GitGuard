using System.Collections.Immutable;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Services;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>
/// 指纹盐的本地存储（07-缓存设计 §3、§5、§9 / DR-02）：首次运行生成 32 字节随机盐并以原子写入落盘，
/// 之后每次读取复用，保证同一机器上指纹跨 run 稳定（白名单匹配依赖此稳定性）。
/// 降级契约（07-缓存设计 §9）：文件不存在 / 损坏 → 重新生成；读取或写入失败 → 使用内存临时盐继续，返回警告而非抛异常
/// （指纹不稳定属非致命，阻断扫描的代价高于白名单暂时失配）。
/// Unix 下盐文件权限为 0600、缓存目录为 0700（08-权限设计 §5.1）；Windows 下检测并重置宽泛 ACL（08 §5.2）。
/// </summary>
public sealed class FingerprintSaltStore
{
    /// <summary>盐文件名（仅用于警告文案，避免在日志中暴露完整用户路径）。</summary>
    private const string SALT_FILE_NAME = "fingerprint.salt";

    private readonly string _saltPath;
    private readonly ISecretRandom _random;

    /// <summary>初始化指纹盐存储。</summary>
    /// <param name="saltPath">盐文件路径（通常为 <see cref="UserDataPaths.FingerprintSaltPath"/>）。</param>
    /// <param name="random">密码学安全随机源（生成新盐用）。</param>
    /// <exception cref="ArgumentException"><paramref name="saltPath"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> 为 <see langword="null"/>。</exception>
    public FingerprintSaltStore(string saltPath, ISecretRandom random)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saltPath);
        ArgumentNullException.ThrowIfNull(random);

        _saltPath = Path.GetFullPath(saltPath);
        _random = random;
    }

    /// <summary>
    /// 读取已有盐；不存在时生成并原子写入。任何 IO 失败都降级为内存临时盐（不抛异常）。
    /// </summary>
    /// <returns>盐（恒为 32 字节）、是否为本进程临时生成的标记、警告文案与权限修复记录。</returns>
    public FingerprintSaltLoadResult LoadOrCreate()
    {
        try
        {
            FilePermissionFix? directoryFix = EnsureDirectory(out string? directoryWarning);

            if (File.Exists(_saltPath))
            {
                byte[] existing = File.ReadAllBytes(_saltPath);
                if (existing.Length == Fingerprinter.SALT_BYTES)
                {
                    FilePermissionFix? saltFix = TryHardenSaltFile(out string? hardenWarning);
                    return new FingerprintSaltLoadResult(
                        existing,
                        isTransient: false,
                        CombineWarnings(directoryWarning, hardenWarning),
                        CombineFixes(directoryFix, saltFix));
                }

                return Persist(
                    Fingerprinter.CreateSalt(_random),
                    CombineWarnings(
                        directoryWarning,
                        $"指纹盐文件 {SALT_FILE_NAME} 已损坏（{existing.Length} 字节），已重新生成；历史指纹将无法匹配。"),
                    directoryFix);
            }

            return Persist(Fingerprinter.CreateSalt(_random), directoryWarning, directoryFix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FingerprintSaltLoadResult(
                Fingerprinter.CreateSalt(_random),
                isTransient: true,
                $"指纹盐读取失败（{ex.GetType().Name}），本次使用临时盐；白名单可能需要重新登记。");
        }
    }

    /// <summary>合并两条非致命警告；任一为空时返回另一条。</summary>
    private static string? CombineWarnings(string? first, string? second)
    {
        if (string.IsNullOrEmpty(first))
        {
            return second;
        }

        return string.IsNullOrEmpty(second) ? first : first + " " + second;
    }

    /// <summary>合并目录与盐文件的权限修复记录；均无修复时返回空数组。</summary>
    private static FilePermissionFix[] CombineFixes(FilePermissionFix? directoryFix, FilePermissionFix? saltFix)
    {
        if (directoryFix is null)
        {
            return saltFix is null ? Array.Empty<FilePermissionFix>() : new[] { saltFix };
        }

        return saltFix is null ? new[] { directoryFix } : new[] { directoryFix, saltFix };
    }

    private FingerprintSaltLoadResult Persist(byte[] salt, string? warning, FilePermissionFix? directoryFix = null)
    {
        try
        {
            WriteAtomically(salt);
            return new FingerprintSaltLoadResult(
                salt,
                isTransient: false,
                warning,
                directoryFix is null ? null : new[] { directoryFix });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FingerprintSaltLoadResult(
                salt,
                isTransient: true,
                CombineWarnings(warning, $"指纹盐写入失败（{ex.GetType().Name}），本次使用临时盐；白名单可能需要重新登记。"));
        }
    }

    /// <summary>创建并加固缓存目录；返回修复记录，加固失败时经 <paramref name="warning"/> 返回非致命警告。</summary>
    private FilePermissionFix? EnsureDirectory(out string? warning)
    {
        warning = null;
        string? directory = Path.GetDirectoryName(_saltPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            // 08-权限设计 §5.1：数据目录 0700；已存在时由 FilePermissionHardener 检测并修复
            Directory.CreateDirectory(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        try
        {
            return FilePermissionHardener.HardenDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = $"缓存目录权限加固失败（{ex.GetType().Name}），请手动检查 08-权限设计 §5.1 要求的 0700 权限。";
            return null;
        }
    }

    private FilePermissionFix? TryHardenSaltFile(out string? warning)
    {
        try
        {
            warning = null;
            return FilePermissionHardener.HardenFile(_saltPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = $"指纹盐权限加固失败（{ex.GetType().Name}），请手动检查 08-权限设计 §5.1 要求的 0600 权限。";
            return null;
        }
    }

    /// <summary>先写同目录临时文件再改名（07-缓存设计 §5），避免多进程启动时读到半写文件。</summary>
    private void WriteAtomically(byte[] salt)
    {
        string temporaryPath = _saltPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporaryPath, salt);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporaryPath, _saltPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

/// <summary>
/// <see cref="FingerprintSaltStore.LoadOrCreate"/> 的结果。
/// </summary>
public sealed record FingerprintSaltLoadResult
{
    /// <summary>初始化读取结果。</summary>
    /// <param name="salt">32 字节指纹盐（<see cref="Fingerprinter.SALT_BYTES"/>）。</param>
    /// <param name="isTransient">为 <see langword="true"/> 表示盐仅存在于本进程内存（落盘失败），指纹跨 run 不稳定。</param>
    /// <param name="warning">面向用户的警告文案；无需警告时为 <see langword="null"/>。</param>
    /// <param name="permissionFixes">本次读取期间发生的权限修复（供 <c>FilePermissionFixed</c> 审计事件使用）；无修复时为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="salt"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="salt"/> 长度不为 32 字节。</exception>
    public FingerprintSaltLoadResult(
        byte[] salt,
        bool isTransient,
        string? warning,
        IReadOnlyList<FilePermissionFix>? permissionFixes = null)
    {
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length != Fingerprinter.SALT_BYTES)
        {
            throw new ArgumentException($"指纹盐必须为 {Fingerprinter.SALT_BYTES} 字节（当前 {salt.Length} 字节）。", nameof(salt));
        }

        Salt = salt;
        IsTransient = isTransient;
        Warning = warning;
        PermissionFixes = permissionFixes?.ToImmutableArray() ?? ImmutableArray<FilePermissionFix>.Empty;
    }

    /// <summary>32 字节指纹盐（<see cref="Fingerprinter.SALT_BYTES"/>）。</summary>
    public byte[] Salt { get; }

    /// <summary>为 <see langword="true"/> 表示盐仅存在于本进程内存（落盘失败），指纹跨 run 不稳定。</summary>
    public bool IsTransient { get; }

    /// <summary>面向用户的警告文案；无需警告时为 <see langword="null"/>。</summary>
    public string? Warning { get; }

    /// <summary>本次读取期间发生的权限修复（可能为空）。</summary>
    public ImmutableArray<FilePermissionFix> PermissionFixes { get; }
}
