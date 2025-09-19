# Copilot Instructions for DesktopAssist

Purpose: Windows desktop automation loop driven by iterative multimodal LLM calls (text + screenshot). Keep responses lean, deterministic, and aligned with existing function schema.

## Architecture (read these first)
- Entry: `Program.cs` loads `AppSettings`, optionally launches overlay `AppForm` (status UI) on a background STA thread, then drives `ExecutionEngine.RunAsync(prompt)`.
- Core loop: `ExecutionEngine` captures a screenshot, builds a single "user" message (context + JSON schema instructions + screenshot), sends to LLM, parses JSON -> executes steps -> repeats until `done` or heuristic completion.
- Parsing: `InstructionParser` expects JSON shape `{ "steps": [], "done": string|null }`. It tolerates malformed output via substring extraction & fallback heuristics.
- Action dispatch: `ActionExecutor` maps `InstructionStep.function` to concrete OS input via `NativeInput` (SendInput P/Invokes). Supported functions: `sleep`, `moveto`, `click`, `write` (alias: `type`), `press`, `hotkey`.
- Vision: `ScreenshotService` grabs & optionally downsizes/compresses the primary screen (JPEG). Re-uses last capture within `ReuseScreenshotWithinMs`.
- Settings: `AppSettings` (auto-created `settings.json`, env var `OPENAI_API_KEY` fallback) controls models, timeouts, screenshot scaling, token budgets, adaptive retry/fallback logic.

## LLM Contract (DO NOT DEVIATE)
Return ONLY raw JSON (no markdown fences, no commentary) exactly:
```
{
  "steps": [ { "function": string, "parameters": { ... }, "human_readable_justification": string } ],
  "done": null | string
}
```
Rules:
- When finished: `steps: []` and concise natural-language summary in `done`.
- Each step object must have all three keys even if `parameters` is `{}` or justification is brief.
- Prefer small batches (1-4 steps). Avoid speculative long chains; engine loops anyway.
- Use only supported function names; avoid inventing new ones (they will throw).

## Function Parameter Conventions
- moveto / click: require in-bounds integer `x`,`y` (pixels). Example: `{ "function":"click","parameters":{"x":640,"y":300,"button":"left"},"human_readable_justification":"Open menu"}`
- click extras: `button` (`left|right`), optional `clicks`, `interval_ms`.
- write: `text` (plain ASCII-ish). Non a-z0-9 and space are mostly ignored; avoid punctuation heavy payloads.
- press: either `key` (single) OR `keys` (array or delimited string: `"ctrl+shift+esc"`). Keys map via simple lowercase names (enter, esc, win, ctrl, shift, alt, letters, digits, f5).
- hotkey: prefer `keys` array (e.g. `["win","r"]`). Engine will press combo (down all, then release reverse order).
- sleep: `secs` (double). Keep short (< 2s) unless absolutely needed for UI readiness.

## Heuristics & Verification
- Engine has a heuristic finalization: if a write (matching original request substring) is followed by Enter, it may stop even without `done`. Better: explicitly set `done`.
- Optional verification flow (off by default) re-prompts model with screenshot asking for `{verified, reason, if_not_verified_steps}`; steps share same schema.

## Adaptive / Retry Behavior
- Long header latency triggers model fallback (`FallbackModel`) and token budget halving (`EnableAdaptiveTokenScaling`). Be economical—avoid verbose justifications.
- Empty or unparsable responses cause a constrained retry (smaller `max_completion_tokens`). Returning clean JSON first time avoids timeouts.

## Logging & Diagnostics
- `VerboseNetworkLogging=true` prints prompt, raw/truncated body, timing, token usage fields if present.
- Status UI (overlay) shows either "Thinking..." or current step function + justification; keep justifications short (<60 chars) for readability.

## Adding New Actions (if extending)
1. Add enum-like string in `ActionExecutor` switch. 2. Implement method translating params to `NativeInput`. 3. Update any parser normalization if introducing synonyms.

## DO / AVOID
- DO keep JSON minimal & strictly valid UTF-8. - A single stray quote breaks parsing.
- DO reuse coordinates previously referenced when performing related clicks; consistency aids reliability.
- AVOID returning narrative text, markdown fences, or additional top-level keys.
- AVOID large multi-second sleeps unless application launch truly requires it.

## Example Valid Response
```
{
  "steps": [
    {"function":"press","parameters":{"keys":["win"]},"human_readable_justification":"Open start menu"},
    {"function":"write","parameters":{"text":"notepad"},"human_readable_justification":"Search app"},
    {"function":"press","parameters":{"key":"enter"},"human_readable_justification":"Launch"}
  ],
  "done": null
}
```

Feedback welcome: clarify unclear function semantics, add new action docs, or note recurring parsing issues.
