-- V003_add_repository_registry：桌面端本机仓库注册表（05-数据库设计 §4.8 / GGD-0042）。
-- 仅保存展示入口：移除登记不级联删除 scan_runs / allowlist / hook_installations 历史数据。
-- 幂等约定（05-数据库设计 §6）：全部语句使用 IF NOT EXISTS，脚本由 SchemaMigrator 在单事务内执行。

CREATE TABLE IF NOT EXISTS repositories (
    id            TEXT    PRIMARY KEY,
    repo_path     TEXT    NOT NULL UNIQUE,
    display_name  TEXT    NOT NULL CHECK (length(trim(display_name)) > 0),
    added_at      INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_repositories_added_at ON repositories (added_at DESC);
