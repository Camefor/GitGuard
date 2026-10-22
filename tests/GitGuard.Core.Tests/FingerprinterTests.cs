using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Domain;
using GitGuard.Core.Services;

namespace GitGuard.Core.Tests;

public sealed class FingerprinterTests
{
    [Fact]
    public void CreateSalt_Returns32BytesFromRandomSource()
    {
        byte[] salt = Fingerprinter.CreateSalt(new StubSecretRandom(0x5A));

        salt.Should().HaveCount(Fingerprinter.SALT_BYTES);
        salt.Should().OnlyContain(static b => b == 0x5A);
    }

    [Fact]
    public void CreateSalt_NullRandom_ThrowsArgumentNullException()
    {
        Action act = () => Fingerprinter.CreateSalt(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateSalt_SourceReturningWrongLength_Throws()
    {
        Action act = () => Fingerprinter.CreateSalt(new FixedLengthSecretRandom(16));

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Ctor_WrongSaltLength_Throws()
    {
        Action act = () => CreateFingerprinter(new byte[31]);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Compute_Returns64LowercaseHex()
    {
        var fingerprint = CreateFingerprinter(CreateSalt(0x11)).Compute("secret-value");

        fingerprint.Hex.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_SameSecretAndSalt_StableAcrossInstances()
    {
        byte[] salt = CreateSalt(0x22);

        var first = CreateFingerprinter(salt).Compute("secret-value");
        var second = CreateFingerprinter(salt).Compute("secret-value");

        first.Hex.Should().Be(second.Hex);
    }

    [Fact]
    public void Compute_DifferentSalt_ProducesDifferentFingerprint()
    {
        var first = CreateFingerprinter(CreateSalt(0x01)).Compute("secret-value");
        var second = CreateFingerprinter(CreateSalt(0x02)).Compute("secret-value");

        first.Hex.Should().NotBe(second.Hex);
    }

    [Fact]
    public void Compute_DifferentSecrets_ProduceDifferentFingerprints()
    {
        var fingerprinter = CreateFingerprinter(CreateSalt(0x03));

        fingerprinter.Compute("secret-a").Hex.Should().NotBe(fingerprinter.Compute("secret-b").Hex);
    }

    [Fact]
    public void Compute_SaltParticipatesInDigest()
    {
        string raw = "secret-value";

        var fingerprint = CreateFingerprinter(CreateSalt(0x04)).Compute(raw);
        string unsaltedSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

        fingerprint.Hex.Should().NotBe(unsaltedSha256);
    }

    [Fact]
    public void Compute_MutatingSourceSaltAfterConstruction_DoesNotAffectResult()
    {
        byte[] salt = CreateSalt(0x05);
        var fingerprinter = CreateFingerprinter(salt);
        var before = fingerprinter.Compute("secret-value");

        salt[0] ^= 0xFF;

        fingerprinter.Compute("secret-value").Hex.Should().Be(before.Hex);
    }

    [Fact]
    public void Compute_CanaryToken_FingerprintExposesNoPlaintext()
    {
        var fingerprint = CreateFingerprinter(CreateSalt(0x06)).Compute(TestData.CANARY_TOKEN);

        fingerprint.Hex.Should().MatchRegex("^[0-9a-f]{64}$");
        fingerprint.Hex.Should().NotContain("AKIA");
    }

    [Fact]
    public void Compute_EmptySecret_Throws()
    {
        Action act = () => CreateFingerprinter(CreateSalt(0x07)).Compute(string.Empty);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Compute_NullSecret_ThrowsArgumentNullException()
    {
        Action act = () => CreateFingerprinter(CreateSalt(0x08)).Compute(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static byte[] CreateSalt(byte fill) => Fingerprinter.CreateSalt(new StubSecretRandom(fill));

    private static Fingerprinter CreateFingerprinter(byte[] salt) => new Fingerprinter(salt);

    private sealed class StubSecretRandom : ISecretRandom
    {
        private readonly byte _fill;

        public StubSecretRandom(byte fill) => _fill = fill;

        public byte[] GetBytes(int count) => Enumerable.Repeat(_fill, count).ToArray();
    }

    private sealed class FixedLengthSecretRandom : ISecretRandom
    {
        private readonly int _length;

        public FixedLengthSecretRandom(int length) => _length = length;

        public byte[] GetBytes(int count) => new byte[_length];
    }
}
