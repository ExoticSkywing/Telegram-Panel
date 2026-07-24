# Design: account list status badges

## Scope

This task updates the account list UX and the account-list API payload. It does not add live per-row Telegram checks and does not add a database migration.

## Data contract

### Existing row data

`AccountListItemDto` already returns username, remark, Telegram status cache fields, and local account metadata.

### New lightweight fields

Add optional/local security fields to `AccountListItemDto` and `frontend/src/api/types.ts`:

- `hasSavedTwoFactorPassword: bool` / `hasSavedTwoFactorPassword: boolean`
  - derived from `!string.IsNullOrWhiteSpace(account.TwoFactorPassword)`
  - means the panel has a saved password, not a live Telegram two-factor guarantee
- `recoveryEmailStatus?: string | null`
  - for this iteration, list defaults to `unknown`/`null`; no batch Telegram calls
  - if future code explicitly queries status, it can update the row client-side

## UI layout

Change the account table visual priority:

1. Phone
2. Nickname
3. Telegram status
4. Security badges
5. User ID
6. Category
7. Channel/group counts
8. Username
9. Remark
10. Registration / sync / actions

This satisfies the requested swap: Telegram status moves to the place where username/remark used to be; username/remark move later where Telegram status used to be.

## Badge semantics

Display badges near Telegram status:

- `二级密码 已保存` -> success when local `twoFactorPassword` exists in DB.
- `二级密码 未保存` -> warning when local password is empty. Label says saved-state, not Telegram live-state, to avoid misleading users.
- `找回邮箱 待检测` -> info for unknown list state; tooltip explains it is checked when opening the recovery-email dialog.
- If later row-level queried status is available:
  - bound -> success
  - unbound -> warning
  - pending -> warning/info with pending pattern

## Compatibility

- Additional API fields are backward-compatible.
- Column visibility is preserved by adding a new `securityBadges` column key with default visible.
- No database migration and no high-volume Telegram calls.

## Rollback

- Remove the new DTO fields and badges column.
- Restore the original column order in `Accounts.vue`.
