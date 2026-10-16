namespace GitGuard.Detection.Validation;

/// <summary>
/// Luhn（模 10）校验，用于银行卡号判定（模块设计 Privacy §3 <c>GGD/PRIVACY/CN-BANKCARD</c>）。
/// 自右向左对第二位起的所有数字加倍（结果 &gt; 9 减 9），总和能被 10 整除即通过。
/// </summary>
public static class LuhnChecksum
{
    /// <summary>校验纯数字串是否满足 Luhn 校验。</summary>
    /// <param name="digits">待校验数字串（不得含分隔符）。</param>
    /// <returns>通过返回 <see langword="true"/>；空串或含非 ASCII 数字字符返回 <see langword="false"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="digits"/> 为 <see langword="null"/>。</exception>
    public static bool IsValid(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Length == 0)
        {
            return false;
        }

        int sum = 0;
        bool doubleDigit = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            char c = digits[i];
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            int digit = c - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}
