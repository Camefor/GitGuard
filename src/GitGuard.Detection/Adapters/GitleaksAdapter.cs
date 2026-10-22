using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Detection.Detectors;

namespace GitGuard.Detection.Adapters;

/// <summary>
/// Gitleaks 适配器（FR-090 / ADR-0003）：把外部 <c>gitleaks detect --no-git</c> 作为可选秘密检测引擎接入，
/// 输出统一映射为 <c>GLK/SECRET/*</c> Finding。
/// <para>
/// <c>IDetector</c> 是逐文件流式契约，而 Gitleaks CLI 按目录扫描：适配器为每个 <see cref="FileChange"/>
/// 建立隔离临时目录，仅写入本次变更行（按原顺序），调用 Gitleaks 后按报告 <c>StartLine</c> 映射回
/// 原始行号。临时目录与 JSON 报告包含敏感原文，必须始终在 <c>finally</c> 中删除；
/// 清理失败按 FailClosed 终止扫描，避免原文残留（DR-02）。
/// </para>
/// <para>
/// 失败语义：可执行文件缺失由启动期探测降级（不进入本类）；扫描期非零退出、报告缺失或 JSON 非法
/// → <see cref="DetectionException"/>（<c>DETECTOR_FAILED</c> → 退出码 11），不重试。
/// </para>
/// </summary>
public sealed class GitleaksAdapter : IDetector
{
    /// <summary>检测器名称（IDetector.Name）。</summary>
    public const string DETECTOR_NAME = "gitleaks";

    /// <summary>规则 ID 命名空间前缀（ADR-0003：GLK 与 GGD / USR 隔离）。</summary>
    public const string RULE_ID_PREFIX = "GLK/SECRET/";

    /// <summary>占位规则模式：FindingFactory 仅消费规则 ID / 严重度，模式不参与本适配器匹配。</summary>
    private static readonly RulePattern _placeholderPattern = RulePattern.Create("(?!)");

    private readonly FindingFactory _findingFactory;
    private readonly IGitleaksProcessRunner _processRunner;
    private readonly string _executablePath;
    private readonly TimeSpan _timeout;
    private readonly Severity _severity;
    private readonly HashSet<string> _disabledRuleIds;
    private readonly Dictionary<string, DetectionRule> _ruleCache = new Dictionary<string, DetectionRule>(StringComparer.Ordinal);

    /// <summary>初始化适配器（生产路径：使用默认子进程执行器）。</summary>
    /// <param name="options">适配器配置。</param>
    /// <param name="findingFactory">发现项工厂（脱敏 + 指纹）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><see cref="GitleaksAdapterOptions.ExecutablePath"/> 为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="GitleaksAdapterOptions.Timeout"/> 非正。</exception>
    public GitleaksAdapter(GitleaksAdapterOptions options, FindingFactory findingFactory)
        : this(options, findingFactory, new GitleaksProcessRunner())
    {
    }

    /// <summary>初始化适配器（测试可注入子进程执行替身）。</summary>
    /// <param name="options">适配器配置。</param>
    /// <param name="findingFactory">发现项工厂。</param>
    /// <param name="processRunner">Gitleaks 子进程执行器。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><see cref="GitleaksAdapterOptions.ExecutablePath"/> 为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="GitleaksAdapterOptions.Timeout"/> 非正。</exception>
    public GitleaksAdapter(
        GitleaksAdapterOptions options,
        FindingFactory findingFactory,
        IGitleaksProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(findingFactory);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExecutablePath);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Timeout, TimeSpan.Zero);

        _findingFactory = findingFactory;
        _processRunner = processRunner;
        _executablePath = options.ExecutablePath;
        _timeout = options.Timeout;
        _severity = options.Severity;
        _disabledRuleIds = NormalizeDisabledRuleIds(options.DisabledRuleIds);
    }

    /// <summary>检测器名称（<c>gitleaks</c>）。</summary>
    public string Name => DETECTOR_NAME;

    /// <summary>
    /// 扫描单个文件的变更行：暂存 → <c>gitleaks detect --no-git</c> → JSON 映射 → 清理临时目录。
    /// </summary>
    /// <param name="change">文件变更。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>映射后的发现项流。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="DetectionException">Gitleaks 执行失败、输出非法或临时目录清理失败（FailClosed）。</exception>
    public async IAsyncEnumerable<Finding> DetectAsync(
        FileChange change,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Lines.Count == 0)
        {
            yield break;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "gitguard-gitleaks", Guid.NewGuid().ToString("N"));
        try
        {
            string sourceDirectory = Path.Combine(tempRoot, "source");
            Directory.CreateDirectory(sourceDirectory);
            RestrictDirectoryPermissions(tempRoot);
            StageChangedLines(change, sourceDirectory);

            string reportPath = Path.Combine(tempRoot, "report.json");
            string[] arguments =
            {
                "detect",
                "--no-git",
                "--source",
                sourceDirectory,
                "--report-format",
                "json",
                "--report-path",
                reportPath,
            };

            GitleaksProcessResult result = await _processRunner
                .RunAsync(_executablePath, arguments, _timeout, cancellationToken)
                .ConfigureAwait(false);

            // gitleaks v8 默认在发现泄漏时退出码为 1；0 / 1 均以 JSON 报告为准
            if (result.ExitCode != 0 && result.ExitCode != 1)
            {
                throw new DetectionException(
                    $"gitleaks detect 退出码 {result.ExitCode}；按 FailClosed 终止扫描且不重试。");
            }

            if (!File.Exists(reportPath))
            {
                // gitleaks 在无命中时不生成报告文件（退出码 0）：这是正常空结果，而非执行失败
                if (result.ExitCode == 0)
                {
                    yield break;
                }

                throw new DetectionException(
                    "gitleaks detect 以退出码 1 报告泄漏，但未生成 JSON 报告；按 FailClosed 终止扫描。");
            }

            IReadOnlyList<Finding> findings = ParseReport(reportPath, change);
            foreach (Finding finding in findings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return finding;
            }
        }
        finally
        {
            DeleteTempDirectory(tempRoot);
        }
    }

    /// <summary>
    /// 把变更行按原顺序写入隔离源目录，保留仓库相对路径（使 Gitleaks 的路径类规则语义尽量一致）。
    /// 路径逃逸（<c>..</c>）直接拒绝，禁止写到临时目录之外。
    /// </summary>
    /// <param name="change">文件变更。</param>
    /// <param name="sourceDirectory">隔离源目录绝对路径。</param>
    /// <exception cref="DetectionException">路径非法或文件写入失败（FailClosed）。</exception>
    private static void StageChangedLines(FileChange change, string sourceDirectory)
    {
        string sourceRoot = Path.GetFullPath(sourceDirectory);
        string rootWithSeparator = sourceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? sourceRoot
            : sourceRoot + Path.DirectorySeparatorChar;
        string relativePath = change.Path.Replace('/', Path.DirectorySeparatorChar);
        string stagedPath = Path.GetFullPath(Path.Combine(sourceRoot, relativePath));

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!stagedPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new DetectionException($"文件路径非法，拒绝暂存到 Gitleaks 临时目录：{change.Path}");
        }

        try
        {
            string? directory = Path.GetDirectoryName(stagedPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new StreamWriter(stagedPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            foreach (ChangedLine line in change.Lines)
            {
                writer.Write(line.Text);
                writer.Write('\n');
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DetectionException($"Gitleaks 临时暂存失败：{ex.Message}", ruleId: null, ex);
        }
    }

    /// <summary>取必填字符串字段；缺失或空白视为报告结构非法。</summary>
    /// <param name="element">报告条目。</param>
    /// <param name="propertyName">字段名。</param>
    /// <returns>字段值。</returns>
    /// <exception cref="DetectionException">字段缺失或空白。</exception>
    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        string? value = GetOptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DetectionException($"Gitleaks JSON 报告缺少字段 {propertyName}；按 FailClosed 终止扫描。");
        }

        return value;
    }

    /// <summary>取可选字符串字段；缺失 / null / 非字符串返回 <see langword="null"/>。</summary>
    /// <param name="element">报告条目。</param>
    /// <param name="propertyName">字段名。</param>
    /// <returns>字段值或 <see langword="null"/>。</returns>
    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>取必填整数行号字段。</summary>
    /// <param name="element">报告条目。</param>
    /// <param name="propertyName">字段名。</param>
    /// <returns>字段值。</returns>
    /// <exception cref="DetectionException">字段缺失或不是整数。</exception>
    private static int GetRequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || !value.TryGetInt32(out int result))
        {
            throw new DetectionException($"Gitleaks JSON 报告缺少整数字段 {propertyName}；按 FailClosed 终止扫描。");
        }

        return result;
    }

    /// <summary>
    /// 把 Gitleaks 原始规则 ID 规范化为 <c>GLK/SECRET/&lt;UPPER_SNAKE&gt;</c>：
    /// 大写并仅保留 <c>[A-Z0-9_-]</c>，其余字符替换为 <c>-</c>。
    /// </summary>
    /// <param name="rawRuleId">Gitleaks 原始规则 ID（如 <c>aws-access-token</c>）。</param>
    /// <returns>符合规则 ID 命名契约的 GLK ID。</returns>
    private static string NormalizeRuleId(string rawRuleId)
    {
        var builder = new StringBuilder(RULE_ID_PREFIX, RULE_ID_PREFIX.Length + rawRuleId.Length);
        foreach (char character in rawRuleId.Trim())
        {
            char upper = char.ToUpperInvariant(character);
            bool allowed = (upper >= 'A' && upper <= 'Z')
                || (upper >= '0' && upper <= '9')
                || upper == '-'
                || upper == '_';
            builder.Append(allowed ? upper : '-');
        }

        if (builder.Length == RULE_ID_PREFIX.Length)
        {
            builder.Append("UNKNOWN");
        }

        return builder.ToString();
    }

    /// <summary>规范化 <c>disabled_rules</c>：忽略 GGD / USR 条目；GLK 条目按命名空间写法，其他视为 Gitleaks 原始 ID。</summary>
    /// <param name="disabledRuleIds">配置的禁用规则 ID。</param>
    /// <returns>适配器侧的规范化禁用集合。</returns>
    private static HashSet<string> NormalizeDisabledRuleIds(IReadOnlyCollection<string> disabledRuleIds)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string ruleId in disabledRuleIds)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
            {
                continue;
            }

            string trimmed = ruleId.Trim();
            if (trimmed.StartsWith("GGD/", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("USR/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(trimmed.StartsWith("GLK/", StringComparison.OrdinalIgnoreCase)
                ? trimmed.ToUpperInvariant()
                : NormalizeRuleId(trimmed));
        }

        return result;
    }

    /// <summary>限制临时目录权限（Unix 0700）：目录内的暂存文件与报告含敏感原文，禁止其他用户遍历。</summary>
    /// <param name="directory">临时目录路径。</param>
    private static void RestrictDirectoryPermissions(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>递归删除临时目录；失败时抛 <see cref="DetectionException"/>（原文残留即 FailClosed）。</summary>
    /// <param name="tempRoot">临时目录根。</param>
    /// <exception cref="DetectionException">目录无法删除。</exception>
    private static void DeleteTempDirectory(string tempRoot)
    {
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DetectionException(
                "Gitleaks 临时目录清理失败：报告文件可能包含敏感原文，按 FailClosed 终止扫描。",
                ruleId: null,
                ex);
        }
    }

    /// <summary>
    /// 解析 Gitleaks JSON 报告并映射为 Finding：<c>StartLine</c> 为暂存文件行序号，
    /// 经 <see cref="FileChange.Lines"/> 还原原始行号；规则 ID 统一命名空间化并应用 <c>disabled_rules</c>。
    /// </summary>
    /// <param name="reportPath">报告文件路径。</param>
    /// <param name="change">所属文件变更。</param>
    /// <returns>发现项列表（同一规则同一行去重）。</returns>
    /// <exception cref="DetectionException">报告读取失败或结构与预期不符（FailClosed）。</exception>
    private IReadOnlyList<Finding> ParseReport(string reportPath, FileChange change)
    {
        string json;
        try
        {
            json = File.ReadAllText(reportPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DetectionException($"Gitleaks JSON 报告读取失败：{ex.Message}", ruleId: null, ex);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new DetectionException("Gitleaks JSON 报告解析失败；按 FailClosed 终止扫描。", ruleId: null, ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Null)
            {
                return Array.Empty<Finding>();
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new DetectionException("Gitleaks JSON 报告结构非法（根节点必须为数组）；按 FailClosed 终止扫描。");
            }

            var findings = new List<Finding>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new DetectionException("Gitleaks JSON 报告结构非法（条目必须为对象）；按 FailClosed 终止扫描。");
                }

                string rawRuleId = GetRequiredString(item, "RuleID");
                int startLine = GetRequiredInt32(item, "StartLine");
                string? rawSecret = GetOptionalString(item, "Secret") ?? GetOptionalString(item, "Match");
                if (string.IsNullOrEmpty(rawSecret) || startLine < 1 || startLine > change.Lines.Count)
                {
                    // 无敏感值无法脱敏 / 指纹；行号越界说明报告与暂存文件不一致，均不产出
                    continue;
                }

                string ruleId = NormalizeRuleId(rawRuleId);
                if (_disabledRuleIds.Contains(ruleId))
                {
                    continue;
                }

                int originalLine = change.Lines[startLine - 1].Number;
                string dedupeKey = ruleId
                    + "\n" + originalLine.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "\n" + rawSecret;
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                var rule = GetOrCreateRule(ruleId, item);
                findings.Add(_findingFactory.Create(rule, change, originalLine, rawSecret, _severity));
            }

            return findings;
        }
    }

    /// <summary>获取（并缓存）Gitleaks 规则对应的 <see cref="DetectionRule"/> 载体。</summary>
    /// <param name="ruleId">已规范化的 GLK 规则 ID。</param>
    /// <param name="item">报告条目（提供 Description）。</param>
    /// <returns>规则载体。</returns>
    private DetectionRule GetOrCreateRule(string ruleId, JsonElement item)
    {
        if (_ruleCache.TryGetValue(ruleId, out DetectionRule? cached))
        {
            return cached;
        }

        string? description = GetOptionalString(item, "Description");
        string name = string.IsNullOrWhiteSpace(description) ? $"Gitleaks 规则 {ruleId}" : description.Trim();
        var rule = DetectionRule.Create(
            ruleId,
            name,
            _severity,
            _placeholderPattern,
            RuleSource.Gitleaks);
        _ruleCache.Add(ruleId, rule);
        return rule;
    }
}
