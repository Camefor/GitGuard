using System.Security.Cryptography;
using System.Text;
using GitGuard.Core.Domain;

namespace GitGuard.Core.Services;

/// <summary>
/// 路径匿名器（09-日志设计 §3 / §9.2 第 3 步）：把原始 <c>repo_path</c> 替换为
/// <c>repo_path_hash</c> —— SHA-256 摘要截断 16 字符小写十六进制，避免原始路径写入日志。
/// 同一路径哈希恒定，供日志按仓库关联排查。
/// </summary>
public static class PathAnonymizer
{
    /// <summary><c>repo_path_hash</c> 的长度（09-日志设计 §3：SHA-256 截断 16 字符）。</summary>
    public const int HASH_LENGTH = 16;

    /// <summary>计算路径的匿名哈希；纯计算，不保留输入。</summary>
    /// <param name="repoPath">仓库绝对路径。</param>
    /// <returns>16 字符小写十六进制哈希。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repoPath"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">路径为空或全为空白时抛出（DR-02）。</exception>
    public static string Anonymize(string repoPath)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        DomainGuard.Require(!string.IsNullOrWhiteSpace(repoPath), "DR-02", "repo_path 不能为空");

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(repoPath));
        return Convert.ToHexStringLower(digest)[..HASH_LENGTH];
    }
}
