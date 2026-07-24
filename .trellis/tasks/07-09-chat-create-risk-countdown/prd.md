# 优化频道群组创建风控与倒计时

## Goal

Improve the channel/group creation UX so accounts that were first added to the panel less than 24 hours ago cannot create Telegram channels or groups. When blocked, the user must see the exact reason and remaining cooldown/countdown.

## Requirements

- Use the account's first panel entry time (`Account.CreatedAt`) as the 24-hour cooldown start.
- The cooldown is a hard block: it cannot be bypassed by the existing "ignore risk warning" flow.
- Apply the hard block to every creation entry point:
  - manual channel creation API/UI
  - manual group creation API/UI
  - task-center private channel/group auto-creation
  - any future caller that uses the core channel/group create services
- Keep the existing login-duration risk warning behavior for other sensitive-operation warnings, but do not let it override the new creation cooldown.
- Manual API failures must return a clear message containing:
  - what happened: account is not yet eligible to create channels/groups
  - why: new panel accounts are blocked for 24 hours to reduce Telegram risk
  - countdown/retry information: remaining duration and retry time metadata
- The create page should proactively show cooldown information after account selection, including a live countdown, and prevent submission while the selected account is blocked.
- Task-center private create jobs should not silently swallow cooldown failures; task details should expose recent cooldown/failure reasons so users can diagnose why items failed.
- Preserve existing successful creation behavior and data persistence for accounts older than 24 hours.

## Acceptance Criteria

- [ ] Selecting an account created in the panel less than 24 hours ago on the create channel/group page shows a warning with the reason and live remaining countdown.
- [ ] The create button is disabled or otherwise prevented for blocked accounts, with no "continue anyway" path.
- [ ] Posting directly to `/api/panel/channels` or `/api/panel/groups` with a blocked account returns HTTP 400 with `success=false`, a clear Chinese message, a cooldown code, `retryAtUtc`, and `retryAfterSeconds`.
- [ ] Posting with `ignoreRiskWarning=true` still cannot bypass this hard cooldown.
- [ ] Channel/group create service methods enforce the cooldown before Telegram RPC calls, so task/future callers cannot bypass it.
- [ ] Task-center private channel/group creation records recent failure reasons in task config/details when accounts are blocked by cooldown.
- [ ] Accounts whose `CreatedAt` is 24 hours or older can create channels/groups as before.
- [ ] Frontend TypeScript build and backend solution build pass.

## Notes

- User confirmed on 2026-07-09 that the 24-hour start point should be the first panel entry time (`CreatedAt`), not the latest Telegram login time or estimated Telegram registration time.
- User confirmed on 2026-07-09 that the hard block should cover all creation entry points, not just the manual page.
