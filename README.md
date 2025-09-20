# DeskPilot

DeskPilot lets you tell an AI what you want done on your Windows machine ("Open Notepad and write a to‑do list", "Rename those screenshots and zip them", "Search settings for dark mode") and then it visually looks at your screen, plans a few precise steps, and carries them out automatically—moving the mouse, pressing keys, launching apps—until the goal is finished or a safety step limit is reached.

---
## Key Features
- Full virtual desktop capture with visual overlays: pixel grid (50px minor / 200px major), cursor crosshair, magnified inset, and dimension banner for precise coordinate reasoning.
- Deterministic action schema: narrowly scoped tool set (keyboard, mouse, window focus, launch, clipboard, delays) mapped through a single executor.
- Strict JSON contract with robust parsing (`InstructionParser`) and minimal retries.
- Pluggable model endpoint using OpenAI Responses-compatible API (`OpenAIClient`).
- Optional floating overlay (`AppForm`) showing real-time agent status / current step justification.
- Adaptive configuration via `settings.json` with environment variable fallback for API key.

---
## High-Level Architecture
| Layer | Responsibility | Key Files |
|-------|----------------|-----------|
| Entry / Bootstrap | Load settings, start UI overlay, obtain objective, invoke loop | `Program.cs`, `AppSettings.cs`, `AppForm.cs` |
| Core Loop | Screenshot -> context -> LLM call -> parse -> execute -> repeat | `AutomationEngine.cs` |
| LLM Integration | Compose multi-part request (system + user context + original request + screenshot) and extract model text | `OpenAIClient.cs` |
| Parsing | Validate strict JSON (`steps` + `done`) and enforce field presence | `InstructionParser.cs`, `InstructionModels.cs` |
| Execution | Map `tool` + `args` to native input, window focus, clipboard, etc. | `ExecutionEngine.cs` (`Executor` class) |
| Input / Native | Low-level SendInput, key mapping, cursor mapping, clipboard | `NativeInput.cs` |
| Vision / Capture | Capture + annotate virtual desktop, manage coordinate translation | `Screenshot.cs`, `ScreenSnapshotInfo` |

Flow: `Program` -> `AutomationEngine.RunAsync` -> capture screenshot -> assemble user context -> `OpenAIClient.CallAsync` -> raw JSON -> `InstructionParser.TryParseResponse` -> iterate `Executor.ExecuteAsync` over steps.

---
## JSON Response Contract (Agent Output)
Agent must return ONLY a single JSON object (no markdown fences):
```
{
  "steps": [ { "tool": string, "args": { ... }, "human_readable_justification": string } ],
  "done": null | string
}
```
- 0–4 steps per turn; engine loops until `done` or `MaxSteps`.
- When complete: provide `done` summary and empty `steps` array.
- Every step requires all three keys; `args` must be a JSON object (use `{}` if none).
- Unknown tools are ignored (wasted turn).

### Supported Tools
| Tool | Args Schema | Behavior |
|------|-------------|----------|
| `mouse` | `x:int,y:int,button?,clicks?,interval_ms?,action?` | Image pixel → screen map; optional move-only (`action:"move"`). |
| `press` | `key:string` or `keys:[...]` | Sequential taps (not chord). |
| `hotkey` | `keys:[...]` | Chord: hold modifiers, press normals, release. |
| `write` / `type` | `text:string,interval_ms?` | Unicode char injection. |
| `paste` | `text:string` | Set clipboard then Ctrl+V. |
| `sleep` | `secs:int<=5` | Short delay. |
| `launch` | `command:string` | Win+R, type command, Enter. |
| `focus_window` | `title:string` | Case-insensitive substring match; restore + focus. |

Example turn:
```
{
  "steps": [
    {"tool":"hotkey","args":{"keys":["win","r"]},"human_readable_justification":"Open Run"},
    {"tool":"write","args":{"text":"notepad"},"human_readable_justification":"Type app name"},
    {"tool":"press","args":{"key":"enter"},"human_readable_justification":"Launch"}
  ],
  "done": null
}
```

---
## Building & Running
Requirements: .NET 8.0 (Windows only — uses Win32 APIs). Clone repo and build solution:

```powershell
# Build
dotnet build DeskPilot.sln -c Debug

# Run with interactive prompt acquisition
dotnet run --project DeskPilot/DeskPilot.csproj

# Or pass objective inline
dotnet run --project DeskPilot/DeskPilot.csproj -- "Open notepad and type hello"
```

The first launch creates `settings.json` beside the executable if missing.

### Distribution Binary
Publish a single-file trimmed build:
```powershell
dotnet publish DeskPilot.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```
Artifacts appear under `DeskPilot/bin/Release/net8.0-windows/win-x64/publish`.

---
## Configuration (`settings.json`)
Notable fields (see `AppSettings.cs` for all):
- `ApiKey`: OpenAI-style key (falls back to `OPENAI_API_KEY` env var).
- `Model`: Default model id (e.g. `gpt-4.1`).
- `MaxSteps`: Outer loop ceiling (default 12).
- `StepDelayMs`: Delay between executed steps (human pacing / UI settling).
- `ReducedScreenshotMode`, `ScreenshotMaxWidth/Height`, `FollowupScreenshotMaxWidth/Height`: Resize controls.
- `EnableAdaptiveTokenScaling`, `AdaptiveLatencyMsThreshold`: Shrink token budgets after slow responses.
- `FallbackModel`: Faster model used on latency / failure conditions.
- `ShowProgressOverlay`: Toggle floating status window.
- `ForceEnglishLayoutForTyping`: Ensures consistent scan code mapping when injecting text.

Regenerate defaults by deleting `settings.json` (a new file is created on next run).

---
## Coordinate System & Overlays
The screenshot represents the full virtual desktop (all monitors). Overlays include:
- Grid lines with numeric labels every 200px major / 50px minor.
- Cursor crosshair + coordinates label (`cursor=(x,y)`).
- Magnified inset (6×) around cursor with fine grid every 10px (major every 50px) and center legend.
- Footer banner containing exact image dimensions.

Agents must supply mouse `x,y` in IMAGE pixel space; `ScreenSnapshotInfo.MapFromImagePx` converts to actual screen coordinates, factoring multi‑monitor offsets and DPI.

---
## Parsing & Validation
`InstructionParser.TryParseResponse` enforces:
- `steps` array optional but if present every element must have: non-empty `tool`, `args` object, non-empty `human_readable_justification`.
- `done` may be `null` or a string summary.
Malformed or extraneous content leads to a skipped turn; keep output strictly the JSON object.

---
## Extending the Tool Set
1. Add new case in `Executor.ExecuteAsync` switch (lowercase name).
2. Implement argument extraction + native interaction (reuse `Input`, `Native`, clipboard helpers).
3. Update `.github/copilot-instructions.md` with tool name & schema.
4. (Optional) Provide synthetic test scenario / sample JSON in PR description.

Keep actions idempotent and side-effect minimal per step; prefer explicit small steps over ambiguous macros.

---
## Development Notes
- Low-level input uses `SendInput` with scan codes for reliability (see `NativeInput.cs`). Elevated target windows may reject injection (will log `[SendInput][Error]`).
- Clipboard operations are marshalled through a short STA thread.
- DPI awareness is set to per-monitor V2 early in `Program` for accurate pixel mapping.
- History of executed steps is accumulated and fed back each turn (`actions_history`) to help the model avoid repetition.
- Objective text is provided both raw UTF-8 and Base64 to mitigate accidental corruption or truncation by upstream model transformations.

---
## Security / Safety Considerations
- The agent can send arbitrary keyboard/mouse events: run inside a controlled, non-production environment.
- Avoid providing secrets in the objective; they could be reproduced in emitted steps (e.g., via `paste`).
- Model output is not sandboxed—review before enabling in always-on scenarios.

---
## Roadmap Ideas (Non-binding)
- Optional OCR / text extraction feedback loop.
- Lightweight DOM-like heuristics (detect focused window / control classification).
- Headless test harness simulating window positions.
- Streaming partial plan validation.

---
## License
(Insert license text or reference once chosen.)

---
## Feedback
File issues or PRs for: parsing failures, desired new tools, overlay readability tweaks, or performance regression reports.
