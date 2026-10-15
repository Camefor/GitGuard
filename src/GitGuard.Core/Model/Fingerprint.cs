using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 指纹值对象：SHA-256 的 64 字符十六进制文本（统一小写）。
/// 依据 DDD §4.2，指纹只能由 <c>Fingerprinter</c>（SHA-256 + Salt）产出：
/// 本类型不提供从字节/原文构造的入口，<see cref="FromHex"/> 仅接受并校验既有的十六进制摘要（DR-02）。
/// </summary>
public readonly record struct Fingerprint
{
    private readonly string? _hex;

    private Fingerprint(string hex) => _hex = hex;

    /// <summary>指纹的 64 字符小写十六进制文本；未初始化的 <c>default</c> 值为空字符串。</summary>
    public string Hex => _hex ?? string.Empty;

    /// <summary>
    /// 从 SHA-256 十六进制摘要构造指纹；校验长度与字符集，并统一规范化为小写。
    /// </summary>
    /// <param name="hex">SHA-256 十六进制文本（64 字符，大小写均可）。</param>
    /// <returns>规范化后的指纹。</returns>
    /// <exception cref="InvariantViolationException">为空、长度不为 64 或含非十六进制字符时抛出。</exception>
    public static Fingerprint FromHex(string hex)
    {
        DomainGuard.Require(!string.IsNullOrEmpty(hex), "DR-02", "Fingerprint 不能为空；指纹必须由 Fingerprinter（SHA-256 + Salt）生成");
        DomainGuard.Require(hex.Length == 64, "DR-02", $"Fingerprint 必须为 64 字符的 SHA-256 十六进制文本（当前 {hex.Length} 字符）");
        DomainGuard.Require(hex.All(char.IsAsciiHexDigit), "DR-02", "Fingerprint 含非十六进制字符");

        return new Fingerprint(hex.ToLowerInvariant());
    }

    /// <summary>返回规范化后的十六进制文本。</summary>
    /// <returns>64 字符小写十六进制字符串。</returns>
    public override string ToString() => Hex;
}
