# Design: chat creation cooldown UX

## Scope

This task spans Core services, Web API DTO/endpoints, task-center handlers, and the Vue create UI.

## Data flow

1. `Account.CreatedAt` is the source of truth for the first panel entry time.
2. Core risk logic derives:
   - `availableAtUtc = CreatedAt + 24 hours`
   - `remaining = availableAtUtc - nowUtc`
   - blocked when `remaining > 0`
3. Backend creation entry points return or throw a single cooldown message/code.
4. The operation-account API exposes enough metadata for proactive frontend UX.
5. The create page computes a live countdown from `chatCreateAvailableAtUtc` and current browser time.

## Backend contract

### Core risk service

Extend `AccountRiskService` with chat-creation cooldown helpers:

- `CheckChatCreationCooldown(Account account, string targetName = "频道/群组")`
- result fields: `IsBlocked`, `Message`, `DetailedMessage`, `AvailableAtUtc`, `RetryAfterSeconds`, `Remaining`.
- `EnsureChatCreationAllowed(Account account, string targetName)` for service-level enforcement.

Keep the existing login-duration `CheckLoginDuration` behavior unchanged.

### Service-level enforcement

Inject `AccountRiskService` into:

- `ChannelService`
- `GroupService`

At the beginning of `CreateChannelAsync` / `CreateGroupAsync`:

1. Load the account through `AccountManagementService`.
2. Run `EnsureChatCreationAllowed`.
3. Only then create/connect the Telegram client and call Telegram RPC.

This makes manual APIs, task handlers, and future callers share the same hard guard.

### Manual API response

Update `OperationResultDto` to allow optional structured error fields while preserving existing constructor calls:

- `Code`
- `RetryAtUtc`
- `RetryAfterSeconds`

Before the existing overrideable login-duration warning in `CreateChannelAsync` and `CreateGroupAsync`, run the cooldown check and return:

- HTTP 400
- `success=false`
- `code="CHAT_CREATE_COOLDOWN"`
- `message` with reason and countdown
- `retryAtUtc` and `retryAfterSeconds`

The old `IgnoreRiskWarning` path remains only for the login-duration risk warning and cannot bypass this check.

### Operation account metadata

Extend `OperationAccountDto` with:

- `CreatedAt`
- `ChatCreateAvailableAtUtc`
- `ChatCreateCooldownRemainingSeconds`

`GetOperationAccountsAsync` should compute these fields using the same core risk helper so the frontend does not duplicate eligibility policy.

### Task-center private creation

`ChannelGroupPrivateCreateTaskHandler` already catches per-item failures. Improve it by:

- letting the service-level cooldown throw before Telegram RPC;
- collecting recent per-account failure lines (including cooldown messages);
- writing them back into the task config under a runtime field such as `recent_failures` (bounded list, e.g. latest 20) via `BatchTaskManagementService.UpdateTaskConfigAsync` or `UpdateTaskDraftAsync`.

This avoids schema/database migration while making task details diagnosable.

## Frontend contract

### Types

Update `OperationResult` and `OperationAccount` in `frontend/src/api/types.ts` for the optional backend fields.

### Create page UX

In `ChatCreate.vue`:

- Track the selected account.
- Compute remaining seconds from `chatCreateAvailableAtUtc` (fall back to server `chatCreateCooldownRemainingSeconds` if needed).
- Maintain a timer while the page is mounted to refresh countdown text.
- Show an `el-alert` under account selection when blocked:
  - reason: account joined panel less than 24 hours ago;
  - retry time formatted locally;
  - live remaining countdown.
- Disable the submit button for blocked accounts.
- In submit catch, handle `code === "CHAT_CREATE_COOLDOWN"` separately: show/retain the hard-block alert and do not open the existing risk override dialog.

### Task details UX

In `Tasks.vue`, include private-create `recent_failures` / `error` in `buildPrivateCreateDetails` so task details show recent blocked accounts and reasons.

## Compatibility

- Adding optional fields to JSON DTOs is backward-compatible for existing clients.
- Existing `new OperationResultDto(false, "...")` call sites continue compiling if optional parameters are appended.
- No database migration is required if task failure details are stored in config JSON.
- Existing accounts with `CreatedAt` older than 24 hours continue to work.

## Edge cases

- `CreatedAt == default`: treat as already eligible to avoid permanently blocking old/malformed legacy rows.
- SQLite may round-trip `DateTime.Kind` as unspecified; interpret non-local unspecified timestamps as UTC because the app writes UTC values.
- Browser/server clocks may differ. Backend is authoritative on submit; frontend countdown is advisory/proactive.
