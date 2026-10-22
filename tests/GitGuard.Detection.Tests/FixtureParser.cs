using System.Text;

namespace GitGuard.Detection.Tests;

/// <summary>样本文件中的单条用例（对应一个扫描行）。</summary>
internal sealed class FixtureCase
{
    public FixtureCase(string section, int lineNumber, string text, bool expectHit)
    {
        Section = section;
        LineNumber = lineNumber;
        Text = text;
        ExpectHit = expectHit;
    }

    /// <summary>所属段（<c>positive</c> / <c>negative</c> / <c>edge</c>）。</summary>
    public string Section { get; }

    /// <summary>样本文件中的 1-based 行号（直接作为扫描行号）。</summary>
    public int LineNumber { get; }

    /// <summary>扫描行文本（edge 段已剥离 <c>hit:</c> / <c>miss:</c> 前缀）。</summary>
    public string Text { get; }

    /// <summary>期望命中状态。</summary>
    public bool ExpectHit { get; }
}

/// <summary>单个规则的样本文件（文件名 <c>&lt;CATEGORY&gt;_&lt;RULE&gt;.txt</c> → 规则 ID <c>GGD/&lt;CATEGORY&gt;/&lt;RULE&gt;</c>）。</summary>
internal sealed class FixtureFile
{
    public FixtureFile(string ruleId, IReadOnlyList<FixtureCase> cases)
    {
        RuleId = ruleId;
        Cases = cases;
    }

    /// <summary>目标规则 ID。</summary>
    public string RuleId { get; }

    /// <summary>全部用例。</summary>
    public IReadOnlyList<FixtureCase> Cases { get; }
}

/// <summary>
/// Fixtures 解析器：<c>;</c> 注释与空行跳过；<c>[positive]</c> / <c>[negative]</c> 段整行即扫描文本；
/// <c>[edge]</c> 段要求 <c>hit: </c> / <c>miss: </c> 前缀显式声明期望。
/// 样本中的 <c>{{ALNUM:n}}</c> 等模板占位符在解析时展开：仓库只保存占位符，完整密钥形态仅在测试运行时生成，
/// 避免 GitHub Push Protection 等远端 secret scanning 把构造的测试数据误判为真实凭据。
/// </summary>
internal static class FixtureParser
{
    private const string HIT_PREFIX = "hit: ";
    private const string MISS_PREFIX = "miss: ";

    private static readonly Dictionary<string, string> _templateAlphabets = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ALNUM"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789",
        ["ALPHA"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz",
        ["DIGIT"] = "0123456789",
        ["HEX"] = "0123456789abcdef",
        ["BASE64"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/",
        ["UPPERALNUM"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789",
    };

    /// <summary>Fixtures 目录（随测试输出复制到运行目录）。</summary>
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>加载全部样本文件，按文件名序数排序（保持用例稳定顺序）。</summary>
    /// <returns>样本文件列表。</returns>
    public static IReadOnlyList<FixtureFile> LoadAll()
    {
        var fixtures = new List<FixtureFile>();
        foreach (string file in System.IO.Directory.EnumerateFiles(Directory, "*.txt").OrderBy(path => path, StringComparer.Ordinal))
        {
            fixtures.Add(Parse(file));
        }

        return fixtures;
    }

    private static FixtureFile Parse(string filePath)
    {
        string fileName = System.IO.Path.GetFileName(filePath);
        string ruleId = RuleIdOf(fileName);
        string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
        var cases = new List<FixtureCase>();
        string? section = null;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line.TrimStart('[').TrimEnd(']');
                continue;
            }

            if (section is null)
            {
                throw new InvalidOperationException($"样本文件缺少段标题：{fileName}:{index + 1}");
            }

            bool expectHit;
            string text;
            if (section == "edge")
            {
                if (line.StartsWith(HIT_PREFIX, StringComparison.Ordinal))
                {
                    expectHit = true;
                    text = line.Substring(HIT_PREFIX.Length);
                }
                else if (line.StartsWith(MISS_PREFIX, StringComparison.Ordinal))
                {
                    expectHit = false;
                    text = line.Substring(MISS_PREFIX.Length);
                }
                else
                {
                    throw new InvalidOperationException($"edge 段样本必须带 hit: / miss: 前缀：{fileName}:{index + 1}");
                }
            }
            else
            {
                expectHit = section == "positive";
                text = line;
            }

            cases.Add(new FixtureCase(section, index + 1, ExpandTemplates(text), expectHit));
        }

        return new FixtureFile(ruleId, cases);
    }

    private static string ExpandTemplates(string text)
    {
        if (!text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 32);
        int cursor = 0;
        int sequence = 0;
        while (cursor < text.Length)
        {
            int start = text.IndexOf("{{", cursor, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(text, cursor, text.Length - cursor);
                break;
            }

            int end = text.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new InvalidOperationException($"样本模板占位符缺少结束标记：{text}");
            }

            builder.Append(text, cursor, start - cursor);
            builder.Append(ExpandToken(text.Substring(start + 2, end - start - 2), sequence++));
            cursor = end + 2;
        }

        return builder.ToString();
    }

    private static string ExpandToken(string token, int sequence)
    {
        int separator = token.IndexOf(':');
        if (separator <= 0
            || !int.TryParse(token.AsSpan(separator + 1), out int length)
            || length <= 0
            || length > 4096
            || !_templateAlphabets.TryGetValue(token.Substring(0, separator), out string? alphabet))
        {
            throw new InvalidOperationException($"样本模板占位符格式无效：{{{{{token}}}}}");
        }

        var builder = new StringBuilder(length);
        for (int index = 0; index < length; index++)
        {
            builder.Append(alphabet[((index * 7) + (sequence * 11)) % alphabet.Length]);
        }

        return builder.ToString();
    }

    private static string RuleIdOf(string fileName)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        int separator = stem.IndexOf('_');
        if (separator <= 0 || separator == stem.Length - 1)
        {
            throw new InvalidOperationException($"样本文件名必须为 <CATEGORY>_<RULE>.txt：{fileName}");
        }

        return $"GGD/{stem.Substring(0, separator)}/{stem.Substring(separator + 1)}";
    }
}
