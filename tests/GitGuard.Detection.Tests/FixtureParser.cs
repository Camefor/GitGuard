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
/// </summary>
internal static class FixtureParser
{
    private const string HIT_PREFIX = "hit: ";
    private const string MISS_PREFIX = "miss: ";

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

            cases.Add(new FixtureCase(section, index + 1, text, expectHit));
        }

        return new FixtureFile(ruleId, cases);
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
