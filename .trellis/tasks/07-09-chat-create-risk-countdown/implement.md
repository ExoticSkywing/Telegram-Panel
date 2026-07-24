# Implementation Plan

## 1. Backend cooldown model and enforcement

- Extend `AccountRiskService` with chat-creation cooldown constants, result model, formatting helper, and `EnsureChatCreationAllowed`.
- Update `ChannelService` and `GroupService` constructors to receive `AccountRiskService`.
- In `CreateChannelAsync` / `CreateGroupAsync`, load the account and enforce cooldown before creating or connecting a Telegram client.

## 2. API DTOs and manual create endpoints

- Extend `OperationResultDto` with optional `Code`, `RetryAtUtc`, and `RetryAfterSeconds`.
- Extend `OperationAccountDto` and its mapper with `CreatedAt`, `ChatCreateAvailableAtUtc`, and `ChatCreateCooldownRemainingSeconds`.
- Update `GetOperationAccountsAsync` to accept/use `AccountRiskService`.
- In manual create endpoints, run cooldown check immediately after account lookup and before the existing login-duration warning.
- Return structured `CHAT_CREATE_COOLDOWN` bad-request responses for blocked accounts.

## 3. Task-center private create diagnostics

- Update `ChannelGroupPrivateCreateTaskHandler` to collect bounded recent failure lines when create attempts fail.
- Persist those lines into the task config JSON runtime field (e.g. `recent_failures`) when progress/draft is updated.
- Preserve existing progress semantics and delay behavior.

## 4. Frontend create UX

- Update TypeScript API types for the new optional fields.
- Add countdown formatting/time tracking in `ChatCreate.vue`.
- Display an `el-alert` when the selected account is cooling down.
- Disable the submit button while the selected account is blocked.
- Handle `CHAT_CREATE_COOLDOWN` responses without opening the risk override dialog.

## 5. Frontend task details UX

- Update `Tasks.vue` private-create detail builder to render `recent_failures` and/or `error` lines.
- Keep runtime fields out of edit forms if existing edit sanitization already strips them; extend if necessary.

## 6. Validation

- Run `dotnet build TelegramPanel.sln`.
- Run `cd frontend && pnpm build`.
- If build failures reveal unrelated existing issues, report them separately and keep the cooldown changes build-clean as far as possible.

## Rollback points

- Backend enforcement can be reverted by removing the new risk helper calls from `ChannelService` and `GroupService`.
- Frontend proactive UX can be reverted independently because backend remains authoritative.
- Task diagnostic config writes are additive and can be removed without data migration.
