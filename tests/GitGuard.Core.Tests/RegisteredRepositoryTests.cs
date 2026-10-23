using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

/// <summary>已登记仓库领域模型测试（05-数据库设计 §4.8 / GGD-0042）：构造不变式、路径规范化与身份相等性。</summary>
public sealed class RegisteredRepositoryTests
{
    [Fact]
    public void Create_ValidValues_PopulatesFieldsWithUuidV7()
    {
        var repository = RegisteredRepository.Create(
            TestData.SampleRepoPath,
            "示例仓库",
            TestData.SampleUtcNow);

        repository.Id.Should().NotBe(Guid.Empty);
        repository.Id.Version.Should().Be(7);
        repository.RepoPath.Should().Be(TestData.SampleRepoPath);
        repository.DisplayName.Should().Be("示例仓库");
        repository.AddedAt.Should().Be(TestData.SampleUtcNow);
    }

    [Fact]
    public void Create_PathWithTrailingSeparator_TrimsSeparator()
    {
        string pathWithSeparator = TestData.SampleRepoPath + Path.DirectorySeparatorChar;

        var repository = RegisteredRepository.Create(
            pathWithSeparator,
            "示例仓库",
            TestData.SampleUtcNow);

        repository.RepoPath.Should().Be(TestData.SampleRepoPath);
    }

    [Fact]
    public void Create_DisplayNameWithSurroundingWhitespace_TrimsName()
    {
        var repository = RegisteredRepository.Create(
            TestData.SampleRepoPath,
            "  示例仓库  ",
            TestData.SampleUtcNow);

        repository.DisplayName.Should().Be("示例仓库");
    }

    [Fact]
    public void Create_NullPath_ThrowsArgumentNullException()
    {
        Action act = () => RegisteredRepository.Create(null!, "示例仓库", TestData.SampleUtcNow);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_NullDisplayName_ThrowsArgumentNullException()
    {
        Action act = () => RegisteredRepository.Create(TestData.SampleRepoPath, null!, TestData.SampleUtcNow);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyOrWhitespacePath_ThrowsInvariantViolation(string path)
    {
        Action act = () => RegisteredRepository.Create(path, "示例仓库", TestData.SampleUtcNow);

        act.Should().Throw<InvariantViolationException>().WithMessage("*RepoPath 不能为空*");
    }

    [Fact]
    public void Create_RelativePath_ThrowsInvariantViolation()
    {
        Action act = () => RegisteredRepository.Create("relative/repo", "示例仓库", TestData.SampleUtcNow);

        act.Should().Throw<InvariantViolationException>().WithMessage("*绝对路径*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyOrWhitespaceDisplayName_ThrowsInvariantViolation(string displayName)
    {
        Action act = () => RegisteredRepository.Create(TestData.SampleRepoPath, displayName, TestData.SampleUtcNow);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DisplayName 不能为空*");
    }

    [Fact]
    public void Create_NonUtcAddedAt_ThrowsInvariantViolation()
    {
        var localTime = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(8));

        Action act = () => RegisteredRepository.Create(TestData.SampleRepoPath, "示例仓库", localTime);

        act.Should().Throw<InvariantViolationException>().WithMessage("*UTC*");
    }

    [Fact]
    public void Restore_EmptyId_ThrowsInvariantViolation()
    {
        Action act = () => RegisteredRepository.Restore(Guid.Empty, TestData.SampleRepoPath, "示例仓库", TestData.SampleUtcNow);

        act.Should().Throw<InvariantViolationException>().WithMessage("*Id 不能为空*");
    }

    [Fact]
    public void Restore_PreservesIdentityAndNormalizesFields()
    {
        var id = Guid.CreateVersion7();

        var repository = RegisteredRepository.Restore(
            id,
            TestData.SampleRepoPath + Path.DirectorySeparatorChar,
            "  示例仓库  ",
            TestData.SampleUtcNow);

        repository.Id.Should().Be(id);
        repository.RepoPath.Should().Be(TestData.SampleRepoPath);
        repository.DisplayName.Should().Be("示例仓库");
    }

    [Fact]
    public void Equals_SameId_ReturnsTrueRegardlessOfOtherFields()
    {
        var id = Guid.CreateVersion7();
        var first = RegisteredRepository.Restore(id, TestData.SampleRepoPath, "甲", TestData.SampleUtcNow);
        var second = RegisteredRepository.Restore(id, TestData.SampleRepoPath, "乙", TestData.SampleUtcNow.AddMinutes(1));

        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        var first = RegisteredRepository.Create(TestData.SampleRepoPath, "甲", TestData.SampleUtcNow);
        var second = RegisteredRepository.Create(TestData.SampleRepoPath, "甲", TestData.SampleUtcNow);

        first.Equals(second).Should().BeFalse();
        first.Equals(null).Should().BeFalse();
    }
}
