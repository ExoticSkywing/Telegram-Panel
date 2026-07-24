# Implementation Plan

## 1. Backend DTO

- Extend `AccountListItemDto` with `HasSavedTwoFactorPassword` and a nullable recovery-email status field.
- Update `ToDto(Account account)` to derive `HasSavedTwoFactorPassword` from `account.TwoFactorPassword`.
- Keep recovery-email list field unset/unknown for now; do not call Telegram while listing accounts.

## 2. Frontend types

- Extend `AccountListItem` with the new optional fields.

## 3. Account list table layout

- Reorder columns in `Accounts.vue`:
  - move Telegram status after nickname
  - add visible `securityBadges` column after Telegram status
  - move username and remark after channel/group counts
- Add `securityBadges` to column visibility options.

## 4. Badge rendering helpers

- Add helper functions for two-factor saved-state badge.
- Add helper functions for recovery-email unknown/known badge.
- Add styles for compact badge grouping.
- If `openEmailDialog('recovery')` explicitly loads live status, update the corresponding row's in-memory recovery status so the badge can improve without a full reload.

## 5. Validation

- Run `cd frontend && corepack pnpm build`.
- Attempt backend build if `dotnet` exists; otherwise record that this environment lacks the SDK.
- Run `git diff --check`.
