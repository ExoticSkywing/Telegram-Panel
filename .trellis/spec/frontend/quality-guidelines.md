# Quality Guidelines

> Code quality standards for frontend development.

---

## Overview

<!--
Document your project's quality standards here.

Questions to answer:
- What patterns are forbidden?
- What linting rules do you enforce?
- What are your testing requirements?
- What code review standards apply?
-->

(To be filled by the team)

---

## Forbidden Patterns

<!-- Patterns that should never be used and why -->

(To be filled by the team)

---

## Required Patterns

<!-- Patterns that must always be used -->

## Scenario: Message-box cancellation is not a fatal frontend error

### 1. Scope / Trigger
- Trigger: an async Vue event handler awaits `ElMessageBox.confirm`, `prompt`, or another Element Plus message-box API.
- Element Plus rejects the promise with the action string `cancel` or `close` when the operator dismisses the dialog.

### 2. Signatures
```ts
function isExpectedUiCancellation(error: unknown): boolean

app.config.errorHandler = (error: unknown) => void
```

### 3. Contracts
- `cancel` and `close` are expected control-flow outcomes, not application failures.
- The global Vue error handler must return without dispatching `telegram-panel:error` for these two values.
- All other errors must continue through the existing global error-reporting path.
- Individual actions may catch cancellation locally when they need custom behavior, but they must not convert cancellation into a fatal page overlay.

### 4. Validation & Error Matrix
- `error === 'cancel'` -> ignore; keep the current page usable.
- `error === 'close'` -> ignore; keep the current page usable.
- `error instanceof Error` -> preserve global error reporting.
- HTTP client errors and non-Vue unhandled rejections -> preserve the existing `App.vue` filtering rules.

### 5. Good/Base/Bad Cases
- Good: operator opens a destructive confirmation dialog, clicks Cancel, and the dialog closes without any error overlay.
- Base: operator confirms the action and the original API workflow continues unchanged.
- Bad: the rejected `cancel` string reaches `telegram-panel:error` and renders “页面加载失败”.

### 6. Tests Required
- Run `pnpm build` so `vue-tsc --noEmit` validates the global handler signature.
- Manual: click “踢出其他设备（已选）”, then Cancel; assert no API request and no fatal overlay.
- Manual: close the same dialog with the top-right close button; assert the same result.
- Error-path: throw a real `Error` from a Vue handler; assert the existing global error reporting still runs.

### 7. Wrong vs Correct
#### Wrong
```ts
app.config.errorHandler = (error) => {
  window.dispatchEvent(new CustomEvent('telegram-panel:error', { detail: error }))
}
```

#### Correct
```ts
app.config.errorHandler = (error) => {
  if (error === 'cancel' || error === 'close') return
  window.dispatchEvent(new CustomEvent('telegram-panel:error', { detail: error }))
}
```

---

## Testing Requirements

<!-- What level of testing is expected -->

(To be filled by the team)

---

## Code Review Checklist

<!-- What reviewers should check -->

(To be filled by the team)
