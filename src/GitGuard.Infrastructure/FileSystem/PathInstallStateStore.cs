using System.Globalization;
using System.Text.Json;
using GitGuard.Core;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>GitGuard PATH 托管状态的持久化记录（仅记录受管条目，卸载据此精确回滚）。</summary>
internal sealed record PathInstallState
{
    /// <summary>初始化 PATH 托管状态记录。</summary>
    /// <param name="platform">平台标识：<c>windows</c> / <c>linux</c> / <c>macos</c>。</param>
    /// <param name="targetDirectory">写入用户 PATH 的目录。</param>
    /// <param name="executablePath">写入时的 GitGuard 可执行文件绝对路径。</param>
    /// <param name="profilePath">Shell profile 路径；Windows 为 <see langword="null"/>。</param>
    /// <param name="installedAt">安装时间（UTC）。</param>
    /// <exception cref="ArgumentException">平台 / 目录 / 可执行文件路径为空或全空白。</exception>
    internal PathInstallState(
        string platform,
        string targetDirectory,
        string executablePath,
        string? profilePath,
        DateTimeOffset installedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        Platform = platform;
        TargetDirectory = targetDirectory;
        ExecutablePath = executablePath;
        ProfilePath = profilePath;
        InstalledAt = installedAt;
    }

    /// <summary>平台标识：<c>windows</c> / <c>linux</c> / <c>macos</c>。</summary>
    internal string Platform { get; }

    /// <summary>写入用户 PATH 的目录。</summary>
    internal string TargetDirectory { get; }

    /// <summary>写入时的 GitGuard 可执行文件绝对路径。</summary>
    internal string ExecutablePath { get; }

    /// <summary>Shell profile 路径；Windows 为 <see langword="null"/>。</summary>
    internal string? ProfilePath { get; }

    /// <summary>安装时间（UTC）。</summary>
    internal DateTimeOffset InstalledAt { get; }
}

/// <summary>
/// PATH 托管状态文件存储（<c>&lt;data&gt;/path-install.json</c>）：原子写入、损坏视为未安装。
/// 状态文件只包含路径与时间戳，不含密钥或用户内容。
/// </summary>
internal sealed class PathInstallStateStore
{
    /// <summary>状态文件 Schema 标识（内部文件，不对外承诺）。</summary>
    internal const string SCHEMA = "gitguard.path.state.v1";

    private const string SCHEMA_PROPERTY = "schema";
    private const string PLATFORM_PROPERTY = "platform";
    private const string TARGET_DIRECTORY_PROPERTY = "target_directory";
    private const string EXECUTABLE_PATH_PROPERTY = "executable_path";
    private const string PROFILE_PATH_PROPERTY = "profile_path";
    private const string INSTALLED_AT_PROPERTY = "installed_at";

    private readonly string _statePath;

    /// <summary>初始化状态存储。</summary>
    /// <param name="statePath">状态文件绝对路径。</param>
    /// <exception cref="ArgumentException"><paramref name="statePath"/> 为空或全空白。</exception>
    internal PathInstallStateStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = Path.GetFullPath(statePath);
    }

    /// <summary>读取状态；文件不存在、损坏或字段缺失时返回 <see langword="null"/>（不抛异常）。</summary>
    /// <returns>状态记录或 <see langword="null"/>。</returns>
    internal PathInstallState? Load()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(_statePath);
            using var document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(SCHEMA_PROPERTY, out JsonElement schema)
                || schema.GetString() != SCHEMA
                || !root.TryGetProperty(PLATFORM_PROPERTY, out JsonElement platform)
                || !root.TryGetProperty(TARGET_DIRECTORY_PROPERTY, out JsonElement target)
                || !root.TryGetProperty(EXECUTABLE_PATH_PROPERTY, out JsonElement executable)
                || !root.TryGetProperty(INSTALLED_AT_PROPERTY, out JsonElement installedAt))
            {
                return null;
            }

            string? platformValue = platform.GetString();
            string? targetValue = target.GetString();
            string? executableValue = executable.GetString();
            if (string.IsNullOrWhiteSpace(platformValue)
                || string.IsNullOrWhiteSpace(targetValue)
                || string.IsNullOrWhiteSpace(executableValue)
                || !installedAt.TryGetDateTimeOffset(out DateTimeOffset installedAtValue))
            {
                return null;
            }

            string? profileValue = root.TryGetProperty(PROFILE_PATH_PROPERTY, out JsonElement profile)
                && profile.ValueKind == JsonValueKind.String
                    ? profile.GetString()
                    : null;
            return new PathInstallState(platformValue, targetValue, executableValue, profileValue, installedAtValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>原子写入状态文件（同目录临时文件 + 覆盖移动）。</summary>
    /// <param name="state">状态记录。</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="PathException">状态文件不可写。</exception>
    internal void Save(PathInstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            string directory = Path.GetDirectoryName(_statePath)
                ?? throw new PathException(PathException.WRITE_PERMISSION_DENIED, "无法解析 PATH 状态文件目录。");
            Directory.CreateDirectory(directory);

            string tempPath = _statePath + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString(SCHEMA_PROPERTY, SCHEMA);
                    writer.WriteString(PLATFORM_PROPERTY, state.Platform);
                    writer.WriteString(TARGET_DIRECTORY_PROPERTY, state.TargetDirectory);
                    writer.WriteString(EXECUTABLE_PATH_PROPERTY, state.ExecutablePath);
                    if (state.ProfilePath is null)
                    {
                        writer.WriteNull(PROFILE_PATH_PROPERTY);
                    }
                    else
                    {
                        writer.WriteString(PROFILE_PATH_PROPERTY, state.ProfilePath);
                    }

                    writer.WriteString(
                        INSTALLED_AT_PROPERTY,
                        state.InstalledAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
                    writer.WriteEndObject();
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _statePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PathException(
                PathException.WRITE_PERMISSION_DENIED,
                $"无法写入 PATH 状态文件：{ex.Message}",
                ex);
        }
    }

    /// <summary>删除状态文件；不存在时静默返回。</summary>
    /// <exception cref="PathException">状态文件不可删除。</exception>
    internal void Delete()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                File.Delete(_statePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PathException(
                PathException.WRITE_PERMISSION_DENIED,
                $"无法删除 PATH 状态文件：{ex.Message}",
                ex);
        }
    }
}
