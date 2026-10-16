-- V001_initial_schema：GitGuard 审计库初始 schema（05-数据库设计 §4 / §5）。
-- 幂等约定（05-数据库设计 §6）：全部语句使用 IF NOT EXISTS，崩溃后重放安全；
-- 本脚本由 SchemaMigrator 在单事务内执行，成功后才写入 schema_version。
-- 注：`detection_rules`（05-数据库设计 §4.7）列结构未定义，V001 不建表，待 P2 以新迁移脚本引入。

CREATE TABLE IF NOT EXISTS scan_runs (
    id                TEXT    PRIMARY KEY,
    repo_path         TEXT    NOT NULL,
    scan_type         TEXT    NOT NULL CHECK (scan_type IN ('PreCommit', 'PrePush', 'Manual', 'Bypass')),
    started_at        INTEGER NOT NULL,
    duration_ms       INTEGER NULL,
    result            TEXT    NOT NULL CHECK (result IN ('Clean', 'Warned', 'Blocked', 'Failed')),
    exit_code         INTEGER NOT NULL DEFAULT 0,
    gitguard_version  TEXT    NOT NULL,
    git_version       TEXT    NULL,
    summary_json      TEXT    NULL,
    CHECK (result <> 'Failed' OR exit_code >= 10)
);

CREATE TABLE IF NOT EXISTS findings (
    id              TEXT    PRIMARY KEY,
    run_id          TEXT    NOT NULL,
    rule_id         TEXT    NOT NULL,
    file_path       TEXT    NOT NULL,
    line_number     INTEGER NOT NULL,
    severity        TEXT    NOT NULL CHECK (severity IN ('Critical', 'High', 'Medium', 'Low')),
    fingerprint     TEXT    NOT NULL CHECK (length(fingerprint) = 64),
    snippet_masked  TEXT    NOT NULL CHECK (length(snippet_masked) <= 32),
    commit_sha      TEXT    NULL,
    action          TEXT    NOT NULL,
    FOREIGN KEY (run_id) REFERENCES scan_runs (id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS allowlist (
    id            TEXT    PRIMARY KEY,
    rule_id       TEXT    NULL,
    path_pattern  TEXT    NULL,
    fingerprint   TEXT    NULL,
    reason        TEXT    NOT NULL CHECK (length(reason) >= 5),
    created_at    INTEGER NOT NULL,
    created_by    TEXT    NOT NULL,
    expires_at    INTEGER NULL,
    CHECK (rule_id IS NOT NULL OR path_pattern IS NOT NULL OR fingerprint IS NOT NULL),
    CHECK (fingerprint IS NULL OR length(fingerprint) = 64)
);

CREATE TABLE IF NOT EXISTS hook_installations (
    repo_path                  TEXT    NOT NULL,
    hook_type                  TEXT    NOT NULL CHECK (hook_type IN ('pre-commit', 'pre-push', 'post-commit')),
    installed_at               INTEGER NOT NULL,
    version                    TEXT    NOT NULL,
    original_hook_backup_path  TEXT    NULL,
    status                     TEXT    NOT NULL DEFAULT 'Active' CHECK (status IN ('Active', 'Replaced', 'Uninstalled')),
    PRIMARY KEY (repo_path, hook_type)
);

CREATE TABLE IF NOT EXISTS audit_logs (
    seq           INTEGER PRIMARY KEY AUTOINCREMENT,
    occurred_at   INTEGER NOT NULL,
    category      TEXT    NOT NULL CHECK (category IN ('Scan', 'Hook', 'Allowlist', 'Bypass', 'Error')),
    event_type    TEXT    NOT NULL,
    payload_json  TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_findings_run_id ON findings (run_id);
CREATE INDEX IF NOT EXISTS ix_findings_fingerprint ON findings (fingerprint);
CREATE INDEX IF NOT EXISTS ix_findings_rule_severity ON findings (rule_id, severity);
CREATE INDEX IF NOT EXISTS ix_scan_runs_repo_time ON scan_runs (repo_path, started_at DESC);
CREATE INDEX IF NOT EXISTS ix_allowlist_fingerprint ON allowlist (fingerprint);
CREATE INDEX IF NOT EXISTS ix_allowlist_rule_path ON allowlist (rule_id, path_pattern);
