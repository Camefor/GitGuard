namespace GitGuard.Detection.Validation;

/// <summary>
/// Shannon 熵计算（模块设计 Secret §6）：<c>H = -Σ p(i) · log2(p(i))</c>。
/// 以字符频率统计；对 Base64 / 十六进制等 ASCII 密钥与按字节统计等价。
/// 用作 AWS-SECRET 等弱启发规则的高熵判定，阈值由检测器配置（默认 4.0）。
/// </summary>
public static class ShannonEntropy
{
    /// <summary>计算文本的 Shannon 熵（比特/字符）。</summary>
    /// <param name="value">待计算文本（密钥片段）。</param>
    /// <returns>熵值；空串返回 0。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 <see langword="null"/>。</exception>
    public static double Calculate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return 0.0;
        }

        var counts = new Dictionary<char, int>(value.Length);
        foreach (char c in value)
        {
            counts[c] = counts.TryGetValue(c, out int count) ? count + 1 : 1;
        }

        double length = value.Length;
        double entropy = 0.0;
        foreach (int count in counts.Values)
        {
            double probability = count / length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }
}
