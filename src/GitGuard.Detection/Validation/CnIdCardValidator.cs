namespace GitGuard.Detection.Validation;

/// <summary>
/// 18 位居民身份证校验位（GB 11643-1999，模块设计 Privacy §3 <c>GGD/PRIVACY/CN-IDCARD-18</c>）：
/// 前 17 位加权求和后按模 11 取校验码；同时做出生日期合理性检查（月 01-12、日 01-31）。
/// </summary>
public static class CnIdCardValidator
{
    /// <summary>模 11 余数到校验码的映射（0 → '1'、1 → '0'、2 → 'X'，…）。</summary>
    private const string CHECK_CODES = "10X98765432";

    /// <summary>前 17 位加权因子（GB 11643-1999 表 1）。</summary>
    private static readonly int[] _weights = new[] { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };

    /// <summary>校验 18 位身份证号。</summary>
    /// <param name="idCard">待校验文本；必须为 18 位（17 位数字 + 数字或 <c>X</c>/<c>x</c> 校验码）。</param>
    /// <returns>校验位与出生日期均合法返回 <see langword="true"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="idCard"/> 为 <see langword="null"/>。</exception>
    public static bool IsValid(string idCard)
    {
        ArgumentNullException.ThrowIfNull(idCard);
        if (idCard.Length != 18)
        {
            return false;
        }

        int sum = 0;
        for (int i = 0; i < 17; i++)
        {
            char c = idCard[i];
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            sum += (c - '0') * _weights[i];
        }

        if (char.ToUpperInvariant(idCard[17]) != CHECK_CODES[sum % 11])
        {
            return false;
        }

        // 出生日期（第 7-14 位为 YYYYMMDD）：月份 01-12、日 01-31
        int month = ((idCard[10] - '0') * 10) + (idCard[11] - '0');
        int day = ((idCard[12] - '0') * 10) + (idCard[13] - '0');
        return month is >= 1 and <= 12 && day is >= 1 and <= 31;
    }
}
