namespace GitGuard.Detection.Validation;

/// <summary>
/// IBAN（ISO 13616）模 97 校验，用于国际银行账号判定（模块设计 Privacy §3 <c>GGD/PRIVACY/IBAN</c>）。
/// 将国家代码与校验位（前 4 位）移至末尾，字母按 A=10…Z=35 展开为数字，
/// 对逐字符累积余数取模 97，结果等于 1 即通过。
/// </summary>
public static class IbanChecksum
{
    /// <summary>IBAN 最短长度（国家代码 2 + 校验位 2 + 基本账号 11）。</summary>
    private const int MIN_LENGTH = 15;

    /// <summary>IBAN 最长长度（ISO 13616 上限）。</summary>
    private const int MAX_LENGTH = 34;

    /// <summary>校验 IBAN 文本（不含空格等分隔符）。</summary>
    /// <param name="iban">待校验的 IBAN；必须为大写字母 / ASCII 数字。</param>
    /// <returns>格式与模 97 校验均通过返回 <see langword="true"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="iban"/> 为 <see langword="null"/>。</exception>
    public static bool IsValid(string iban)
    {
        ArgumentNullException.ThrowIfNull(iban);
        if (iban.Length is < MIN_LENGTH or > MAX_LENGTH)
        {
            return false;
        }

        if (!char.IsAsciiLetterUpper(iban[0])
            || !char.IsAsciiLetterUpper(iban[1])
            || !char.IsAsciiDigit(iban[2])
            || !char.IsAsciiDigit(iban[3]))
        {
            return false;
        }

        int remainder = 0;
        for (int i = 4; i < iban.Length; i++)
        {
            if (!Accumulate(iban[i], ref remainder))
            {
                return false;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (!Accumulate(iban[i], ref remainder))
            {
                return false;
            }
        }

        return remainder == 1;
    }

    private static bool Accumulate(char c, ref int remainder)
    {
        if (char.IsAsciiDigit(c))
        {
            remainder = ((remainder * 10) + (c - '0')) % 97;
            return true;
        }

        if (char.IsAsciiLetterUpper(c))
        {
            // 字母值 10-35 占两位，因此整体先乘 100 再累加。
            remainder = ((remainder * 100) + (c - 'A' + 10)) % 97;
            return true;
        }

        return false;
    }
}
