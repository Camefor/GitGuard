using FluentAssertions;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Detection.Rules;

namespace GitGuard.Detection.Tests;

/// <summary>
/// 内置规则样本测试（GGD-0006 验收）：逐行驱动目标规则所属检测器，
/// 断言「命中 / 不命中」与设计一致，并校验 50 条规则样本覆盖度（每规则 ≥3 正 / ≥3 负）。
/// </summary>
public sealed class BuiltinRuleFixtureTests
{
    /// <summary>将全部样本行展开为理论数据：规则 ID、行号、扫描文本、期望命中。</summary>
    /// <returns>xUnit 理论数据。</returns>
    public static TheoryData<string, int, string, bool> FixtureCases()
    {
        var data = new TheoryData<string, int, string, bool>();
        foreach (FixtureFile fixture in FixtureParser.LoadAll())
        {
            foreach (FixtureCase testCase in fixture.Cases)
            {
                data.Add(fixture.RuleId, testCase.LineNumber, testCase.Text, testCase.ExpectHit);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public async Task FixtureLine_ShouldMatchExpectedHitOrMiss(string ruleId, int lineNumber, string text, bool expectHit)
    {
        IDetector detector = TestSupport.CreateDetector(CategoryOf(ruleId));
        var change = new FileChange(TestSupport.NEUTRAL_SCAN_PATH, new[] { new ChangedLine(lineNumber, text) });

        List<Finding> findings = await TestSupport.CollectAsync(detector, change);

        Finding[] target = findings.Where(finding => finding.RuleId == ruleId).ToArray();
        target.Should().HaveCount(
            expectHit ? 1 : 0,
            because: $"{ruleId} 对「{text}」的期望是{(expectHit ? "命中" : "不命中")}（样本行 {lineNumber}）");

        if (expectHit)
        {
            target[0].LineNumber.Should().Be(lineNumber);
            target[0].FilePath.Should().Be(TestSupport.NEUTRAL_SCAN_PATH);
        }
    }

    [Fact]
    public void Fixtures_ShouldCoverEveryBuiltinRuleWithMinimumSamples()
    {
        var rules = BuiltinRuleLoader.LoadEmbedded();
        IReadOnlyList<FixtureFile> fixtures = FixtureParser.LoadAll();

        fixtures.Select(fixture => fixture.RuleId)
            .Should().BeEquivalentTo(rules.Select(rule => rule.RuleId), "每条内置规则都必须有同名样本文件");

        foreach (FixtureFile fixture in fixtures)
        {
            string because = $"{fixture.RuleId} 的样本数量不满足设计下限（≥3 正 / ≥3 负 / edge 命中与不命中各 ≥1）";
            fixture.Cases.Count(testCase => testCase.Section == "positive").Should().BeGreaterThanOrEqualTo(3, because);
            fixture.Cases.Count(testCase => testCase.Section == "negative").Should().BeGreaterThanOrEqualTo(3, because);
            fixture.Cases.Count(testCase => testCase.Section == "edge" && testCase.ExpectHit).Should().BeGreaterThanOrEqualTo(1, because);
            fixture.Cases.Count(testCase => testCase.Section == "edge" && !testCase.ExpectHit).Should().BeGreaterThanOrEqualTo(1, because);
        }
    }

    private static string CategoryOf(string ruleId) => ruleId.Split('/')[1];
}
