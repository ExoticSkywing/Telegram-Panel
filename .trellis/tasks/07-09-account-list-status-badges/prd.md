# 优化账号列表状态徽章布局

## Goal

Improve the account list so operators can see Telegram health/status, username/remark, and account security attributes more directly. The list should place Telegram health/status information earlier, move username/remark to the lower-priority area currently occupied later in the table, and surface two-factor/recovery-email attributes as badge-style indicators similar to the account detail/email status prompt.

## Confirmed Facts

- Current account list table lives in `frontend/src/views/Accounts.vue`.
- Current visible column order is: phone, nickname, username, remark, userId, category, channel/group counts, Telegram status, registration time, last sync, actions.
- Current `AccountListItem` API payload does not include two-factor enabled or recovery-email bound status.
- `AccountDetail` includes `twoFactorPassword`, which is the locally saved password, not necessarily a live Telegram two-factor status.
- The existing per-account endpoint `GET /api/panel/accounts/{id}/two-factor/recovery-email` calls Telegram and returns `hasTwoFactorPassword`, `hasRecoveryEmail`, and pending email pattern, but it is currently used on demand in dialogs, not for every account row.
- Running live Telegram status checks for every account row can be slow and can increase Telegram/API risk on large lists.
- User chose the low-risk data source on 2026-07-09: use existing/local data now; do not batch-call Telegram from the list. Two-factor badge may use the local saved-password field; recovery-email badge should be shown as unknown/needs detection unless already loaded by an explicit per-account action.

## Requirements

- Move Telegram status earlier in the account list, swapping its visual position with username/remark as requested.
- Keep username and remark visible, but lower priority than Telegram status.
- Add badge-style display for account security attributes in an easy-to-see place on each row.
- Badges should represent:
  - local two-factor password state, derived from whether a password is saved in the database
  - recovery email state, shown as unknown/needs detection unless live status has been explicitly queried elsewhere
- Preserve existing column visibility controls where practical.
- Avoid making the account list significantly slower or triggering high-volume Telegram calls.
- Do not add a database migration for this task.

## Acceptance Criteria

- [x] Account list column order places Telegram status where username/remark used to be, and username/remark later where Telegram status used to be.
- [x] Each account row shows badge-style security attributes in a clearly visible area near the high-priority status information.
- [x] Badge labels/colors distinguish saved two-factor password vs missing saved password, and recovery-email unknown/needs-detection state.
- [x] The account list does not issue one Telegram API request per row just to render badges.
- [x] Existing filters, pagination, selection, column visibility, and row actions continue to work.
- [x] Frontend type-check/build passes.
- [x] Backend build passes where `dotnet` is available. (`dotnet` is not installed in this environment; build could not be run here.)

## Out of Scope

- Persisting authoritative Telegram two-factor/recovery-email status in new database columns.
- Automatically refreshing recovery-email status for every account row.
- Changing the account detail/recovery-email binding flow beyond reflecting status if it is already queried.
