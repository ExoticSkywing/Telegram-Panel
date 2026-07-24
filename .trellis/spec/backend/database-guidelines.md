# Database Guidelines

> Database patterns and conventions for this project.

---

## Overview

<!--
Document your project's database conventions here.

Questions to answer:
- What ORM/query library do you use?
- How are migrations managed?
- What are the naming conventions for tables/columns?
- How do you handle transactions?
-->

(To be filled by the team)

---

## Query Patterns

<!-- How should queries be written? Batch operations? -->

(To be filled by the team)

---

## Migrations

<!-- How to create and run migrations -->

(To be filled by the team)

## Scenario: Docker SQLite Image Upgrade

### 1. Scope / Trigger

- Trigger: replacing the Telegram Panel container image while reusing an existing Docker `/data` mount.
- This contract applies because application startup can migrate both the SQLite schema and persistent-storage markers. A rollback therefore has to restore the program and its pre-upgrade data together.

### 2. Signatures

```text
Host bind mount: ./docker-data:/data
Database:        /data/telegram-panel.db
Sessions:        /data/sessions/
Credentials:     /data/admin_auth.json
Local settings:  /data/appsettings.local.json
Connection env:  ConnectionStrings__DefaultConnection=Data Source=/data/telegram-panel.db
```

Backup and validation commands:

```bash
docker compose stop telegram-panel
cp -a docker-data /path/outside-the-repository/docker-data
sqlite3 docker-data/telegram-panel.db ".backup '/path/outside-the-repository/telegram-panel.db.backup'"
sqlite3 -readonly /path/outside-the-repository/telegram-panel.db.backup 'PRAGMA quick_check;'
```

### 3. Contracts

- Back up the entire persistent directory, not only `telegram-panel.db`; Sessions, credentials, settings, uploads, migration markers, and future state are part of the same recovery unit.
- Create the authoritative snapshot while the application container is stopped.
- Keep a second SQLite `.backup` and require `PRAGMA quick_check` to return exactly `ok` before upgrading.
- Record pre-upgrade table counts, account count, Session filename set, old image ID, and deployment configuration.
- A rollback must restore both the old image and the complete pre-upgrade persistent snapshot. Do not run an old binary against a database already migrated by a newer binary.
- Store backups outside the Git worktree with permissions that prevent other users from reading Sessions or credentials.

### 4. Validation & Error Matrix

| Condition | Required action |
| --- | --- |
| Backup `quick_check` is not `ok` | Abort; restart the old container and investigate. |
| WAL/SHM disappears during clean shutdown | Do not treat this alone as data loss; validate the main database and independent `.backup`. |
| Startup reports the database changed during storage inspection, exits once, then starts cleanly | Accept only after restart count stops growing, storage migration markers appear, and all data checks pass. |
| Restart count keeps increasing or health never becomes healthy | Stop the new container and perform the old-image plus old-data rollback. |
| Existing table, account, or Session counts decrease without a documented migration | Treat as migration failure and roll back. |
| New schema tables or migration-history rows appear while old data remains | Expected for a successful forward migration. |

### 5. Good / Base / Bad Cases

- Good: stopped snapshot, independent `.backup`, both integrity checks pass, the new image becomes healthy, old counts do not decrease, and the rollback image remains available.
- Base: SQLite removes empty `-wal` or `-shm` files during shutdown; the main database and `.backup` are still `ok`, so validation continues.
- Bad: copy only the live `.db`, discard the old image, then assume an HTTP 200 alone proves the migration preserved accounts and Sessions.

### 6. Tests Required

- Assert the rendered Compose configuration mounts the intended host directory at `/data`.
- Assert `PRAGMA quick_check` is `ok` for both the pre-upgrade backup and the post-upgrade database.
- Compare every pre-existing table count before and after; assert no unexplained decrease.
- Assert the account count and Session filename set match the baseline.
- Assert the credential and local-settings files still exist and are unchanged unless the release explicitly migrates them.
- Assert container health, restart-count stability, `/healthz`, authentication-state API behavior, image ID, and runtime working directory.
- Assert the rollback image tag and the external data snapshot still exist after validation.

### 7. Wrong vs Correct

#### Wrong

```bash
cp docker-data/telegram-panel.db /tmp/telegram-panel.db
docker compose pull && docker compose up -d
# Delete the old image immediately after /healthz returns 200.
```

This can miss WAL-backed writes and all non-database persistent state, and it leaves no schema-compatible rollback.

#### Correct

```bash
docker compose stop telegram-panel
cp -a docker-data /secure-backups/telegram-panel/pre-upgrade/docker-data
sqlite3 docker-data/telegram-panel.db ".backup '/secure-backups/telegram-panel/pre-upgrade/telegram-panel.db.backup'"
sqlite3 -readonly /secure-backups/telegram-panel/pre-upgrade/telegram-panel.db.backup 'PRAGMA quick_check;'
# Preserve/tag the old image, deploy the new image, then verify health and data counts.
```

---

## Naming Conventions

<!-- Table names, column names, index names -->

(To be filled by the team)

---

## Common Mistakes

<!-- Database-related mistakes your team has made -->

(To be filled by the team)
