using FluentAssertions;
using GitGuard.Application.Allowlist;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Tests;

/// <summary>
/// 白名单用例测试（<c>06-API接口设计</c> §5.5 / FR-040）：新增时的领域校验与错误码映射、
/// 删除不存在条目的退出码语义，以及列表查询条件透传。
/// </summary>
public sealed class AllowlistUseCaseTests
{
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Add_ValidEntry_NormalizesConditionsAndUsesClock()
    {
        var store = new RecordingAllowlistStore();
        var useCase = new AllowlistAddUseCase(store, new FixedClock(_now));

        AllowlistEntry entry = useCase.Execute(
            "  GGD/SECRET/AWS-AKID  ",
            "  src/**  ",
            new string('A', 64),
            "  已知误报登记  ",
            "tester");

        store.Added.Should().ContainSingle().Which.Should().BeSameAs(entry);
        entry.RuleId.Should().Be("GGD/SECRET/AWS-AKID");
        entry.PathPattern.Should().Be("src/**");
        entry.Fingerprint.Should().NotBeNull();
        entry.Fingerprint!.Value.Hex.Should().Be(new string('a', 64));
        entry.Reason.Should().Be("已知误报登记");
        entry.CreatedAt.Should().Be(_now);
        entry.CreatedBy.Should().Be("tester");
    }

    [Fact]
    public void Add_NoCondition_ThrowsValidationFailed()
    {
        var useCase = new AllowlistAddUseCase(new RecordingAllowlistStore(), new FixedClock(_now));

        Action act = () => useCase.Execute(null, null, null, "缺少匹配条件", "tester");

        act.Should().Throw<AllowlistException>()
            .Which.ErrorCode.Should().Be(AllowlistException.ALLOWLIST_VALIDATION_FAILED);
    }

    [Fact]
    public void Add_InvalidFingerprint_ThrowsValidationFailed()
    {
        var useCase = new AllowlistAddUseCase(new RecordingAllowlistStore(), new FixedClock(_now));

        Action act = () => useCase.Execute(null, null, "not-a-fingerprint", "指纹格式非法", "tester");

        act.Should().Throw<AllowlistException>()
            .Which.ErrorCode.Should().Be(AllowlistException.ALLOWLIST_VALIDATION_FAILED);
    }

    [Fact]
    public void Add_ReasonTooShort_ThrowsValidationFailed()
    {
        var useCase = new AllowlistAddUseCase(new RecordingAllowlistStore(), new FixedClock(_now));

        Action act = () => useCase.Execute("GGD/SECRET/AWS-AKID", null, null, "abcd", "tester");

        act.Should().Throw<AllowlistException>()
            .Which.ErrorCode.Should().Be(AllowlistException.ALLOWLIST_VALIDATION_FAILED);
    }

    [Fact]
    public void Add_NullArguments_Throw()
    {
        var store = new RecordingAllowlistStore();
        var useCase = new AllowlistAddUseCase(store, new FixedClock(_now));

        Action createWithoutStore = () => _ = new AllowlistAddUseCase(null!, new FixedClock(_now));
        Action createWithoutClock = () => _ = new AllowlistAddUseCase(store, null!);
        Action executeWithoutReason = () => useCase.Execute("GGD/SECRET/AWS-AKID", null, null, null!, "tester");
        Action executeWithoutCreatedBy = () => useCase.Execute("GGD/SECRET/AWS-AKID", null, null, "登记人为空", null!);

        createWithoutStore.Should().Throw<ArgumentNullException>();
        createWithoutClock.Should().Throw<ArgumentNullException>();
        executeWithoutReason.Should().Throw<ArgumentNullException>();
        executeWithoutCreatedBy.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Remove_ExistingEntry_DeletesById()
    {
        var store = new RecordingAllowlistStore { RemoveResult = true };
        var useCase = new AllowlistRemoveUseCase(store);
        var id = Guid.CreateVersion7();

        useCase.Execute(id);

        store.RemovedIds.Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public void Remove_MissingEntry_ThrowsNotFound()
    {
        var store = new RecordingAllowlistStore { RemoveResult = false };
        var useCase = new AllowlistRemoveUseCase(store);

        Action act = () => useCase.Execute(Guid.CreateVersion7());

        act.Should().Throw<AllowlistException>()
            .Which.ErrorCode.Should().Be(AllowlistException.ALLOWLIST_NOT_FOUND);
    }

    [Fact]
    public void Remove_EmptyId_ThrowsNotFoundWithoutStoreCall()
    {
        var store = new RecordingAllowlistStore { RemoveResult = true };
        var useCase = new AllowlistRemoveUseCase(store);

        Action act = () => useCase.Execute(Guid.Empty);

        act.Should().Throw<AllowlistException>()
            .Which.ErrorCode.Should().Be(AllowlistException.ALLOWLIST_NOT_FOUND);
        store.RemovedIds.Should().BeEmpty();
    }

    [Fact]
    public void Remove_NullStore_Throws()
    {
        Action act = () => _ = new AllowlistRemoveUseCase(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void List_PassesQueryThroughAndReturnsPage()
    {
        var entry = AllowlistEntry.Create(
            "GGD/SECRET/AWS-AKID",
            null,
            null,
            "列表条目记录",
            _now,
            "tester");
        var expectedPage = new AllowlistPage(new[] { entry }, 1);
        var store = new RecordingAllowlistStore { Page = expectedPage };
        var useCase = new AllowlistListUseCase(store);
        var query = new AllowlistQuery(limit: 5, offset: 10);

        AllowlistPage page = useCase.Execute(query);

        store.LastQuery.Should().BeSameAs(query);
        page.Should().BeSameAs(expectedPage);
        page.TotalCount.Should().Be(1);
        page.Entries.Should().ContainSingle().Which.Id.Should().Be(entry.Id);
    }

    [Fact]
    public void List_NullArguments_Throw()
    {
        Action createWithoutStore = () => _ = new AllowlistListUseCase(null!);
        Action executeWithoutQuery = () => new AllowlistListUseCase(new RecordingAllowlistStore()).Execute(null!);

        createWithoutStore.Should().Throw<ArgumentNullException>();
        executeWithoutQuery.Should().Throw<ArgumentNullException>();
    }

    private sealed class RecordingAllowlistStore : IAllowlistStore
    {
        internal List<AllowlistEntry> Added { get; } = new List<AllowlistEntry>();

        internal List<Guid> RemovedIds { get; } = new List<Guid>();

        internal bool RemoveResult { get; set; }

        internal AllowlistPage? Page { get; set; }

        internal AllowlistQuery? LastQuery { get; private set; }

        public void AddAllowlistEntry(AllowlistEntry entry) => Added.Add(entry);

        public bool RemoveAllowlistEntry(Guid id)
        {
            RemovedIds.Add(id);
            return RemoveResult;
        }

        public AllowlistPage QueryAllowlistEntries(AllowlistQuery query)
        {
            LastQuery = query;
            return Page ?? new AllowlistPage(Array.Empty<AllowlistEntry>(), 0);
        }

        public IReadOnlyList<AllowlistEntry> ListAllAllowlistEntries() => Added;
    }
}
