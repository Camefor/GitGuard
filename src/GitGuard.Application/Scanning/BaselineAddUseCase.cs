using GitGuard.Core;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 基线增量登记用例（GGD-0062 / FR-061 桌面端补强）：把单个已核实的发现项指纹写入仓库根
/// <c>.gitguard-baseline.json</c>，用于把历史存量噪声标记为已知、后续 <c>scan --all</c> 不再阻断。
/// 只影响全量扫描的默认基线消费，不改变 <c>pre-commit</c> / <c>pre-push</c> 的拦截（误报治理指南 §5.6）。
/// 指纹写入不携带任何敏感原文（DR-02）。
/// </summary>
public sealed class BaselineAddUseCase
{
    private readonly string _repositoryRoot;

    /// <summary>初始化基线增量登记用例。</summary>
    /// <param name="repositoryRoot">仓库工作区根目录绝对路径。</param>
    /// <exception cref="ArgumentException"><paramref name="repositoryRoot"/> 为空或全空白。</exception>
    public BaselineAddUseCase(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = repositoryRoot;
    }

    /// <summary>向仓库根基线文件增量登记一条指纹。</summary>
    /// <param name="fingerprintHex">64 位十六进制指纹（大小写均可）。</param>
    /// <param name="ruleId">规则 ID（写入条目元数据，供人工核对）。</param>
    /// <param name="filePath">仓库相对路径（写入条目元数据，供人工核对）。</param>
    /// <returns>登记结果（<see cref="BaselineAddResult.AddedEntries"/> 为 0 表示指纹已在基线中）。</returns>
    /// <exception cref="ArgumentException">任一参数为空或全空白。</exception>
    /// <exception cref="BaselineException">指纹 / 规则 ID 非法，或基线文件读取 / 写入失败。</exception>
    public BaselineAddResult Execute(string fingerprintHex, string ruleId, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintHex);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Fingerprint fingerprint;
        try
        {
            fingerprint = Fingerprint.FromHex(fingerprintHex.Trim());
        }
        catch (InvariantViolationException ex)
        {
            throw new BaselineException(
                BaselineException.BASELINE_INVALID,
                $"指纹不合法：{ex.Message}",
                ex);
        }

        if (!RuleIdFormat.IsValid(ruleId))
        {
            throw new BaselineException(
                BaselineException.BASELINE_INVALID,
                $"规则 ID 不符合命名契约：{ruleId}");
        }

        string path = BaselineFile.ResolveDefaultPath(_repositoryRoot);
        int added = BaselineFile.AddEntry(path, fingerprint.Hex, ruleId, filePath);
        return new BaselineAddResult(path, added);
    }
}
