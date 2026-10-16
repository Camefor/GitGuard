namespace GitGuard.Infrastructure.Tests;

/// <summary>测试用敏感样本（16-测试方案 §7：严禁真实敏感数据，一律使用 sentinel 占位）。</summary>
internal static class TestData
{
    /// <summary>金丝雀 Token 样本；GitAccess 层返回原始差异文本，DR-02 脱敏发生在 Finding 构建阶段。</summary>
    internal const string CanaryToken = "AKIA_SENTINEL_4f2a1b9c8d7e6f5a4b3c2d1e0f9a8b7c";
}
