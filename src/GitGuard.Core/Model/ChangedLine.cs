using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 差异变更行：<c>FileChange</c> 的最小扫描单元，对应 <c>git diff -U0</c> 中一条新增 / 修改行。
/// 仅承载「1-based 行号 + 行文本」，不携带来源侧信息（-U0 差分天然只含新增行，DR-04）。
/// </summary>
public sealed class ChangedLine
{
    /// <summary>初始化变更行。</summary>
    /// <param name="number">新文件中的 1-based 行号。</param>
    /// <param name="text">行文本（不含换行符；空行允许为空串）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException"><paramref name="number"/> 小于 1 时抛出。</exception>
    public ChangedLine(int number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        DomainGuard.Require(number >= 1, "DDD §4.1", $"变更行行号为 1-based，必须 ≥ 1（当前 {number}）");

        Number = number;
        Text = text;
    }

    /// <summary>新文件中的 1-based 行号。</summary>
    public int Number { get; }

    /// <summary>行文本（不含换行符）。</summary>
    public string Text { get; }
}
