namespace GitGuard.Infrastructure.FileSystem;

/// <summary>
/// 用户级数据目录与固定文件路径的解析（05-数据库设计 §1、07-缓存设计 §3、08-权限设计 §4、09-日志设计 §7、10-异常处理设计 §8）。
/// 平台约定：
/// <list type="bullet">
///   <item><description>Windows：<c>%LOCALAPPDATA%\GitGuard\</c>。</description></item>
///   <item><description>macOS：<c>~/Library/Application Support/GitGuard/</c>。</description></item>
///   <item><description>Linux：<c>$XDG_DATA_HOME/gitguard/</c>，未设置时 <c>~/.local/share/gitguard/</c>。</description></item>
/// </list>
/// 用户级配置文件统一为 <c>~/.config/gitguard/config.yml</c>（FR-071，三平台同路径）。
/// 所有路径均为纯计算，不创建目录、不访问磁盘。
/// </summary>
public sealed class UserDataPaths
{
    /// <summary>Windows 数据目录环境变量（08-权限设计 §5.2）。</summary>
    private const string LOCAL_APP_DATA_ENV = "LOCALAPPDATA";

    /// <summary>Linux XDG 数据目录环境变量（05-数据库设计 §1）。</summary>
    private const string XDG_DATA_HOME_ENV = "XDG_DATA_HOME";

    /// <summary>数据目录名（Windows 为 <c>GitGuard</c>，其余平台为 <c>gitguard</c>）。</summary>
    private const string WINDOWS_DIRECTORY_NAME = "GitGuard";

    /// <summary>Unix 数据目录名。</summary>
    private const string UNIX_DIRECTORY_NAME = "gitguard";

    /// <summary>审计数据库文件名（05-数据库设计 §1）。</summary>
    private const string AUDIT_DATABASE_FILE_NAME = "audit.db";

    /// <summary>缓存目录名（07-缓存设计 §3）。</summary>
    private const string CACHE_DIRECTORY_NAME = "cache";

    /// <summary>指纹盐文件名（07-缓存设计 §3）。</summary>
    private const string FINGERPRINT_SALT_FILE_NAME = "fingerprint.salt";

    /// <summary>日志目录名（09-日志设计 §7）。</summary>
    private const string LOG_DIRECTORY_NAME = "logs";

    /// <summary>文件日志名（09-日志设计 §7）。</summary>
    private const string LOG_FILE_NAME = "gitguard.log";

    /// <summary>崩溃日志目录名（10-异常处理设计 §8）。</summary>
    private const string CRASH_DIRECTORY_NAME = "crash";

    /// <summary>用户级配置文件名（FR-071）。</summary>
    private const string USER_CONFIG_FILE_NAME = "config.yml";

    /// <summary>以显式根目录初始化（测试注入用）；生产路径请用 <see cref="Default"/>。</summary>
    /// <param name="dataDirectory">
    /// 数据目录根（audit.db / cache / logs / crash 的父目录）；<see langword="null"/> 时按当前平台解析。
    /// </param>
    /// <param name="userConfigDirectory">
    /// 用户级配置目录（config.yml 的父目录）；<see langword="null"/> 时为 <c>&lt;home&gt;/.config/gitguard</c>。
    /// </param>
    /// <exception cref="ArgumentException">显式传入的目录路径为空或全空白。</exception>
    public UserDataPaths(string? dataDirectory = null, string? userConfigDirectory = null)
    {
        if (dataDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        }

        if (userConfigDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userConfigDirectory);
        }

        DataDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(dataDirectory ?? ResolveDefaultDataDirectory()));
        UserConfigDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(userConfigDirectory ?? ResolveDefaultUserConfigDirectory()));
    }

    /// <summary>按当前平台解析的默认路径集合。</summary>
    public static UserDataPaths Default { get; } = new UserDataPaths();

    /// <summary>数据目录根（Windows <c>%LOCALAPPDATA%\GitGuard</c>；Unix 见类型注释）。</summary>
    public string DataDirectory { get; }

    /// <summary>审计数据库路径（05-数据库设计 §1：<c>&lt;data&gt;/audit.db</c>）。</summary>
    public string AuditDatabasePath => Path.Combine(DataDirectory, AUDIT_DATABASE_FILE_NAME);

    /// <summary>缓存目录（07-缓存设计 §3：<c>&lt;data&gt;/cache</c>）。</summary>
    public string CacheDirectory => Path.Combine(DataDirectory, CACHE_DIRECTORY_NAME);

    /// <summary>指纹盐文件路径（07-缓存设计 §3：<c>&lt;data&gt;/cache/fingerprint.salt</c>）。</summary>
    public string FingerprintSaltPath => Path.Combine(CacheDirectory, FINGERPRINT_SALT_FILE_NAME);

    /// <summary>日志目录（09-日志设计 §7：<c>&lt;data&gt;/logs</c>）。</summary>
    public string LogDirectory => Path.Combine(DataDirectory, LOG_DIRECTORY_NAME);

    /// <summary>默认文件日志路径（09-日志设计 §7：<c>&lt;data&gt;/logs/gitguard.log</c>）。</summary>
    public string LogFilePath => Path.Combine(LogDirectory, LOG_FILE_NAME);

    /// <summary>崩溃日志目录（10-异常处理设计 §8：<c>&lt;data&gt;/crash</c>）。</summary>
    public string CrashDirectory => Path.Combine(DataDirectory, CRASH_DIRECTORY_NAME);

    /// <summary>用户级配置目录（<c>~/.config/gitguard</c>）。</summary>
    public string UserConfigDirectory { get; }

    /// <summary>用户级配置文件路径（FR-071：<c>~/.config/gitguard/config.yml</c>）。</summary>
    public string UserConfigPath => Path.Combine(UserConfigDirectory, USER_CONFIG_FILE_NAME);

    private static string ResolveDefaultDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            string? localAppData = Environment.GetEnvironmentVariable(LOCAL_APP_DATA_ENV);
            string root = string.IsNullOrWhiteSpace(localAppData)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : localAppData;
            return CombineChecked(root, WINDOWS_DIRECTORY_NAME);
        }

        if (OperatingSystem.IsMacOS())
        {
            string home = ResolveHomeDirectory();
            return Path.Combine(home, "Library", "Application Support", WINDOWS_DIRECTORY_NAME);
        }

        string? xdgDataHome = Environment.GetEnvironmentVariable(XDG_DATA_HOME_ENV);
        return string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(ResolveHomeDirectory(), ".local", "share", UNIX_DIRECTORY_NAME)
            : Path.Combine(Path.GetFullPath(xdgDataHome), UNIX_DIRECTORY_NAME);
    }

    private static string ResolveDefaultUserConfigDirectory() =>
        Path.Combine(ResolveHomeDirectory(), ".config", UNIX_DIRECTORY_NAME);

    private static string ResolveHomeDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("无法解析当前用户的主目录路径。");
        }

        return home;
    }

    private static string CombineChecked(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException($"无法解析用户数据目录（环境变量 {LOCAL_APP_DATA_ENV} 与已知文件夹均为空）。");
        }

        return Path.Combine(root, name);
    }
}
