# Error Handling

> How errors are handled in this project.

---

## Overview

<!--
Document your project's error handling conventions here.

Questions to answer:
- What error types do you define?
- How are errors propagated?
- How are errors logged?
- How are errors returned to clients?
-->

(To be filled by the team)

---

## Scenario: Chat Creation Cooldown API Contract

### 1. Scope / Trigger
- Trigger: channel/group creation is a cross-layer API contract (`Core` risk policy → Web API DTO → Vue UX).
- Any hard business block that the frontend must render differently from generic errors must use a stable `code` plus structured retry metadata.

### 2. Signatures
- Manual create endpoints:
  - `POST /api/panel/channels`
  - `POST /api/panel/groups`
- Error DTO shape:
  ```csharp
  public sealed record OperationResultDto(
      bool Success,
      string? Message,
      string? Code = null,
      DateTime? RetryAtUtc = null,
      int? RetryAfterSeconds = null);
  ```
- Cooldown code:
  ```csharp
  AccountRiskService.ChatCreateCooldownCode == "CHAT_CREATE_COOLDOWN"
  ```

### 3. Contracts
- Cooldown source of truth: `Account.CreatedAt` in UTC.
- Affected action: creating Telegram channels or groups, whether invoked by manual API endpoints, task handlers, or future callers of `IChannelService.CreateChannelAsync` / `IGroupService.CreateGroupAsync`.
- Cooldown response fields:
  - `success`: `false`
  - `message`: Chinese human-readable reason plus remaining wait time
  - `code`: `CHAT_CREATE_COOLDOWN`
  - `retryAtUtc`: UTC timestamp when creation may be retried
  - `retryAfterSeconds`: positive integer remaining seconds
- Existing `ignoreRiskWarning` flags may bypass soft login-duration warnings only; they must not bypass cooldown errors.

### 4. Validation & Error Matrix
- `AccountId <= 0` -> `400` with `OperationResultDto(false, "请选择账号")`.
- account not found -> `404` with `OperationResultDto(false, "账号不存在")`.
- `Account.CreatedAt + 24h > nowUtc` -> `400`, `code=CHAT_CREATE_COOLDOWN`, retry metadata present.
- soft login-duration risk and `ignoreRiskWarning != true` -> `400` without cooldown code; frontend may show a risk override dialog.
- eligible account -> continue to Telegram RPC and normal persistence.

### 5. Good/Base/Bad Cases
- Good: an account created 25 hours ago creates a channel normally and no cooldown fields are returned.
- Base: an account created 23 hours ago gets `CHAT_CREATE_COOLDOWN` with `retryAfterSeconds` around 3600.
- Bad: an account created 1 hour ago with `ignoreRiskWarning=true` still gets blocked by `CHAT_CREATE_COOLDOWN`.

### 6. Tests Required
- API test: post channel create with a new account and assert `400`, `success=false`, `code`, `retryAtUtc`, and `retryAfterSeconds > 0`.
- API test: repeat with `ignoreRiskWarning=true` and assert the same cooldown block.
- Service test: call `CreateChannelAsync` / `CreateGroupAsync` with a new account and assert `ChatCreationCooldownException` before Telegram RPC setup.
- API test: account older than 24 hours reaches the normal create path.

### 7. Wrong vs Correct
#### Wrong
```csharp
if (!request.IgnoreRiskWarning && risk.IsRisky)
    return Results.BadRequest(new OperationResultDto(false, risk.Message));

await channelService.CreateChannelAsync(...); // task callers can bypass endpoint checks
```

#### Correct
```csharp
var cooldown = riskService.CheckChatCreationCooldown(account);
if (cooldown.IsBlocked)
    return Results.BadRequest(new OperationResultDto(false, cooldown.Message, cooldown.Code, cooldown.AvailableAtUtc, cooldown.RetryAfterSeconds));

await channelService.CreateChannelAsync(...); // service enforces the same hard guard too
```

---

## Error Types

<!-- Custom error classes/types -->

(To be filled by the team)

---

## Error Handling Patterns

<!-- Try-catch patterns, error propagation -->

(To be filled by the team)

---

## API Error Responses

<!-- Standard error response format -->

(To be filled by the team)

---

## Common Mistakes

<!-- Error handling mistakes your team has made -->

(To be filled by the team)
