using FluentAssertions;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Services;
using GitGuard.Infrastructure.FileSystem;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// 指纹盐存储集成测试（07-缓存设计 §3/§5/§9、DR-02）：真实临时目录验证首次生成 / 复用 / 损坏再生 /
/// 落盘失败降级为内存临时盐（不抛异常）。
/// </summary>
public sealed class FingerprintSaltStoreTests
{
    [Fact]
    public void LoadOrCreate_FirstRun_Persists32ByteSalt()
    {
        using var directory = new TempDirectory();
        string saltPath = CreateSaltPath(directory);
        var store = new FingerprintSaltStore(saltPath, new DeterministicSecretRandom());

        FingerprintSaltLoadResult result = store.LoadOrCreate();

        result.Salt.Should().HaveCount(Fingerprinter.SALT_BYTES);
        result.IsTransient.Should().BeFalse();
        result.Warning.Should().BeNull();
        File.ReadAllBytes(saltPath).Should().Equal(result.Salt);
    }

    [Fact]
    public void LoadOrCreate_SecondRun_ReusesPersistedSalt()
    {
        using var directory = new TempDirectory();
        string saltPath = CreateSaltPath(directory);

        byte[] first = new FingerprintSaltStore(saltPath, new DeterministicSecretRandom()).LoadOrCreate().Salt;
        byte[] second = new FingerprintSaltStore(saltPath, new DeterministicSecretRandom()).LoadOrCreate().Salt;

        second.Should().Equal(first, "指纹跨 run 稳定是白名单匹配的前提");
    }

    [Fact]
    public void LoadOrCreate_CorruptFile_RegeneratesWithWarning()
    {
        using var directory = new TempDirectory();
        string saltPath = CreateSaltPath(directory);
        File.WriteAllBytes(saltPath, new byte[16]);
        var store = new FingerprintSaltStore(saltPath, new DeterministicSecretRandom());

        FingerprintSaltLoadResult result = store.LoadOrCreate();

        result.Salt.Should().HaveCount(Fingerprinter.SALT_BYTES);
        result.IsTransient.Should().BeFalse();
        result.Warning.Should().Contain("已损坏");
        File.ReadAllBytes(saltPath).Should().Equal(result.Salt);
    }

    [Fact]
    public void LoadOrCreate_UnwritablePath_FallsBackToTransientSalt()
    {
        using var directory = new TempDirectory();

        // 盐路径指向一个目录：写入必然失败，用于验证降级契约（07-缓存设计 §9）
        string saltPath = Path.Combine(directory.Root, "cache");
        Directory.CreateDirectory(saltPath);
        var store = new FingerprintSaltStore(saltPath, new DeterministicSecretRandom());

        FingerprintSaltLoadResult result = store.LoadOrCreate();

        result.Salt.Should().HaveCount(Fingerprinter.SALT_BYTES);
        result.IsTransient.Should().BeTrue();
        result.Warning.Should().Contain("临时盐");
    }

    [Fact]
    public void Constructor_RejectsBlankPathOrNullRandom()
    {
        FluentActions.Invoking(() => new FingerprintSaltStore("  ", new DeterministicSecretRandom()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new FingerprintSaltStore("salt", null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LoadOrCreate_OnUnix_RestrictsSaltFileToOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TempDirectory();
        string saltPath = CreateSaltPath(directory);

        new FingerprintSaltStore(saltPath, new DeterministicSecretRandom()).LoadOrCreate();

        File.GetUnixFileMode(saltPath)
            .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite, "08-权限设计 §5.1：盐文件 0600");
    }

    [Fact]
    public void LoadResult_RejectsSaltWithWrongLength()
    {
        FluentActions.Invoking(() => new FingerprintSaltLoadResult(new byte[16], isTransient: false, warning: null))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new FingerprintSaltLoadResult(null!, isTransient: false, warning: null))
            .Should().Throw<ArgumentNullException>();
    }

    /// <summary>盐文件路径（自动创建其父目录，模拟 07-缓存设计 §3 的 cache/ 目录布局）。</summary>
    private static string CreateSaltPath(TempDirectory directory)
    {
        Directory.CreateDirectory(Path.Combine(directory.Root, "cache"));
        return Path.Combine(directory.Root, "cache", "fingerprint.salt");
    }

    /// <summary>确定性随机源：每次调用返回可复现的字节序列，便于断言盐内容与复用语义。</summary>
    private sealed class DeterministicSecretRandom : ISecretRandom
    {
        private byte _next;

        public byte[] GetBytes(int count)
        {
            byte[] bytes = new byte[count];
            for (int i = 0; i < count; i++)
            {
                bytes[i] = _next++;
            }

            return bytes;
        }
    }
}
