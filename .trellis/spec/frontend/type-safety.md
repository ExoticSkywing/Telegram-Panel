# Type Safety

> Type safety patterns in this project.

---

## Overview

<!--
Document your project's type safety conventions here.

Questions to answer:
- What type system do you use?
- How are types organized?
- What validation library do you use?
- How do you handle type inference?
-->

(To be filled by the team)

---

## Scenario: Typed Cooldown Error Handling

### 1. Scope / Trigger
- Trigger: backend `OperationResultDto` and `OperationAccountDto` gained fields used by Vue pages to render a hard business block.
- Frontend API types must mirror backend optional fields instead of parsing message text.

### 2. Signatures
```ts
export interface OperationResult {
  success: boolean
  message?: string | null
  code?: string | null
  retryAtUtc?: string | null
  retryAfterSeconds?: number | null
}

export interface OperationAccount {
  id: number
  displayPhone: string
  nickname?: string | null
  username?: string | null
  isActive: boolean
  categoryId?: number | null
  createdAt: string
  chatCreateAvailableAtUtc?: string | null
  chatCreateCooldownRemainingSeconds?: number | null
}
```

### 3. Contracts
- `code === 'CHAT_CREATE_COOLDOWN'` means a hard block; do not show a "continue anyway" risk dialog.
- `chatCreateAvailableAtUtc` is the proactive UI source for countdown display.
- Backend remains authoritative: submit handlers must still handle `CHAT_CREATE_COOLDOWN` responses even if the proactive countdown is stale.

### 4. Validation & Error Matrix
- missing `chatCreateAvailableAtUtc` -> no proactive block.
- invalid date string -> no proactive block; backend submit may still reject.
- `chatCreateAvailableAtUtc > now` -> show warning and disable submit.
- API response `code=CHAT_CREATE_COOLDOWN` -> update local account cooldown metadata and show non-bypassable warning.

### 5. Good/Base/Bad Cases
- Good: selected account has future `chatCreateAvailableAtUtc`; UI shows a live countdown and disables create.
- Base: selected account has past `chatCreateAvailableAtUtc`; UI allows create.
- Bad: code searches `message.includes('24 小时')` first and opens an override dialog for a hard block.

### 6. Tests Required
- Type-check build must pass with the extended API interfaces.
- Component/manual test: select blocked account -> warning visible, countdown ticks, submit disabled.
- Error-path test: mock `CHAT_CREATE_COOLDOWN` response -> no risk override dialog; warning shown.

### 7. Wrong vs Correct
#### Wrong
```ts
if (message.includes('24 小时')) {
  await showRiskWarning({ message })
}
```

#### Correct
```ts
if (data?.code === 'CHAT_CREATE_COOLDOWN') {
  ElMessage.warning(data.message)
} else if (message.includes('24 小时')) {
  await showRiskWarning({ message })
}
```

---

## Scenario: Account List Security Badges Use Lightweight Local Status

### 1. Scope / Trigger
- Trigger: account-list rows show security attributes such as saved two-factor password state and recovery-email state.
- These badges are rendered on every row, so their list payload must stay cheap and must not fan out into Telegram RPC calls.

### 2. Signatures
```ts
export interface AccountListItem {
  // existing list fields...
  hasSavedTwoFactorPassword: boolean
  recoveryEmailStatus?: string | null
}
```

Backend list DTO shape mirrors this contract:
```csharp
public sealed record AccountListItemDto(
    // existing list fields...
    bool HasSavedTwoFactorPassword,
    string? RecoveryEmailStatus);
```

### 3. Contracts
- `hasSavedTwoFactorPassword` means the panel database has a non-empty saved two-factor password. It is not a live Telegram two-factor guarantee.
- `recoveryEmailStatus` on the list should default to `null` / unknown unless a user-triggered flow has explicitly queried or changed that account's recovery-email state.
- The list endpoint must not call Telegram once per row just to render badges; use local/database fields only.
- If a row-level dialog explicitly calls the recovery-email status endpoint, the Vue row may update its in-memory badge state without reloading the whole list.

### 4. Validation & Error Matrix
- saved two-factor password is non-empty -> badge shows saved state.
- saved two-factor password is empty/whitespace -> badge shows missing saved password, not "Telegram 2FA disabled".
- list `recoveryEmailStatus` is missing/null/unknown -> badge shows needs detection.
- explicit recovery-email status probe succeeds -> row badge may show bound, unbound, or pending.
- explicit recovery-email status probe fails -> row badge may show detection failed, but list loading itself should remain cheap.

### 5. Good/Base/Bad Cases
- Good: account list derives `hasSavedTwoFactorPassword` from the account row and returns `recoveryEmailStatus = null` without Telegram calls.
- Base: opening the recovery-email dialog calls the per-account status endpoint and updates only that row's in-memory badge.
- Bad: account list rendering loops over rows and calls `/two-factor/recovery-email` or another Telegram-backed endpoint for each account.

### 6. Tests Required
- Type-check build must pass with frontend API interfaces matching backend DTO fields.
- Frontend build/manual check: account list renders badges and column visibility still works.
- API/manual check: loading the account list does not produce one Telegram status/recovery-email request per row.

### 7. Wrong vs Correct
#### Wrong
```ts
await Promise.all(rows.map((row) => panelApi.twoFactorRecoveryEmailStatus(row.id)))
```

#### Correct
```ts
const rows = await panelApi.accounts({ page, pageSize })
// Later, only after the operator opens one account's recovery-email dialog:
const status = await panelApi.twoFactorRecoveryEmailStatus(row.id)
```

---

## Type Organization

<!-- Where types are defined, shared types vs local types -->

(To be filled by the team)

---

## Validation

<!-- Runtime validation patterns (Zod, Yup, io-ts, etc.) -->

(To be filled by the team)

---

## Common Patterns

<!-- Type utilities, generics, type guards -->

(To be filled by the team)

---

## Forbidden Patterns

<!-- any, type assertions, etc. -->

(To be filled by the team)
