-- V002_extend_audit_event_categories：审计事件类别扩展（08-权限设计 §7 / 05-数据库设计 §4.5）。
-- 新增 Policy（PolicyOverride）与 Security（FilePermissionFixed）；FailClosed 复用既有 Error 类别。
-- SQLite 无法修改 CHECK 约束，采用「建新表 → 复制 → 删旧表 → 改名」重建；脚本由 SchemaMigrator 在单事务内执行，
-- 失败整体回滚，重放安全。显式复制 seq 以保留 AUTOINCREMENT 序列与既有审计顺序。

CREATE TABLE audit_logs_v2 (
    seq           INTEGER PRIMARY KEY AUTOINCREMENT,
    occurred_at   INTEGER NOT NULL,
    category      TEXT    NOT NULL CHECK (category IN ('Scan', 'Hook', 'Allowlist', 'Bypass', 'Error', 'Policy', 'Security')),
    event_type    TEXT    NOT NULL,
    payload_json  TEXT    NOT NULL
);

INSERT INTO audit_logs_v2 (seq, occurred_at, category, event_type, payload_json)
SELECT seq, occurred_at, category, event_type, payload_json FROM audit_logs;

DROP TABLE audit_logs;

ALTER TABLE audit_logs_v2 RENAME TO audit_logs;
