namespace GitGuard.Application.Configuration;

/// <summary>
/// 组合根可选的配置覆盖（GGD-0046）：在「内置默认 ← 用户级 ← 项目级」三层合并完成后，
/// 由宿主（桌面端设置页）覆盖少量可在运行期调整的字段，不写回任何配置文件。
/// 当前仅覆盖 Gitleaks 适配器开关与可执行文件路径；后续字段按需扩展。
/// </summary>
public sealed record GitGuardConfigOverrides
{
    /// <summary>覆盖 <c>rules.secrets.use_gitleaks</c>；<see langword="null"/> 表示保留配置文件取值。</summary>
    public bool? UseGitleaks { get; init; }

    /// <summary>
    /// 覆盖 <c>rules.secrets.gitleaks_path</c>；<see langword="null"/> 表示在 <c>PATH</c> 中查找。
    /// 仅在 <see cref="UseGitleaks"/> 为 <see langword="true"/> 时参与适配器探测。
    /// </summary>
    public string? GitleaksPath { get; init; }

    /// <summary>把覆盖应用到已合并的有效配置。</summary>
    /// <param name="config">三层合并后的有效配置。</param>
    /// <returns>应用覆盖后的新配置快照（原对象不变）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> 为 <see langword="null"/>。</exception>
    public GitGuardConfig ApplyTo(GitGuardConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config with
        {
            Secrets = config.Secrets with
            {
                UseGitleaks = UseGitleaks ?? config.Secrets.UseGitleaks,
                GitleaksPath = GitleaksPath,
            },
        };
    }
}
