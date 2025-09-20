## DesktopAssist – Focused Build Rules for AI Agents
Purpose: Drive Windows desktop UI via iterative multimodal LLM turns (text + annotated screenshot). Produce minimal, deterministic JSON plans that map directly to executor tools. Anything outside the contract wastes a turn.

### 1. Runtime Architecture (read first)
1. `Program.cs` loads `AppSettings` (env `OPENAI_API_KEY` fallback), optionally spawns overlay UI (`AppForm`) then calls `AutomationEngine.RunAsync` with the user's objective.
2. `AutomationEngine.RunAsync` (loop): capture screenshot (with grid, cursor & inset via `Screenshot.CapturePrimaryPngBase64`), build system + user context, call LLM (`OpenAIClient`), parse with `InstructionParser`, execute each step with `Executor` then repeat until max steps or completion.
3. `InstructionParser.TryParseResponse` expects strict JSON matching `StepsResponse` (`steps` array of `{tool,args,human_readable_justification}` + `done` nullable). Any deviation (extra prose, markdown fences, missing fields) risks rejection.
4. `Executor` maps tools -> concrete actions (keyboard / mouse / window focus) using `NativeInput` / `Input` / `WindowFocus` utilities.
5. Coordinate system: model sees an annotated full-virtual-desktop image; mouse tool consumes image pixel coordinates (NOT normalized screen coords). Mapping performed by `ScreenSnapshotInfo.MapFromImagePx`.

### 2. JSON RESPONSE CONTRACT (DO NOT DEVIATE)
Return ONLY a single JSON object:
{
  "steps": [ { "tool": string, "args": { ... }, "human_readable_justification": string } ],
  "done": null | string   // string = concise final summary
}
Rules:
- Keep 0–4 steps per turn. Engine loops; long speculative chains are trimmed.
- When task complete: set steps = [] and place short natural language summary in `done`.
- All step keys required even if args {}.
- Use only supported tool names below; unrecognized tool silently wastes the turn.

### 3. Supported Tools & Args (match `Executor` switch)
mouse: { x:int, y:int, button:"left|right|middle"?, clicks:1-4?, interval_ms:10-1000?, action:"move"? }  // image-space pixels; engine validates bounds
press: { key:"enter" | keys:["ctrl","s", ...] }  // sequential taps (NOT chord)
hotkey: { keys:["ctrl","shift","esc"] }  // chord: hold modifiers, press normals
write | type: { text:"ascii-like" , interval_ms?:int }  // sends Unicode chars one by one
paste: { text:"..." }  // sets clipboard then Ctrl+V
sleep: { secs:int<=5 }
launch: { command:"notepad" } // Win+R then command + Enter
focus_window: { title:"substring" } // case-insensitive partial match

Notes:
- Provide integer x,y inside the screenshot width/height; reuse prior coordinates when refining clicks.
- For simple Enter after typing, prefer separate steps: write + press{key:"enter"}.
- Avoid large pasted blobs; keep text purposeful.

### 4. Justification Field
`human_readable_justification` appears in overlay (truncate > ~60 chars). Keep it short, imperative: "Open Run dialog", "Type filename".

### 5. Heuristics & Limits
- `AppSettings.MaxSteps` (default 12) ends session; be explicit by setting `done` earlier when objective satisfied.
- If you return no steps and no `done`, loop continues (wasted turn). Always choose one.
- Returning > MaxSteps steps => truncated; include only essential immediate actions.

### 6. Failure / Robustness Considerations
- Parser demands every step has: non-empty tool, args object, non-empty justification. Provide empty object `{}` if no args.
- A malformed response causes retry with reduced token budget—so first response correctness matters.
- Do NOT emit markdown, commentary, or extra top-level keys.

### 7. Coordinate Reasoning Aids
Screenshot overlay supplies: grid (50px minor / 200px major), cursor crosshair, magnified inset, dimension banner (e.g. `image=1920x1080`). Use those to pick precise x,y.

### 8. Extensibility (for maintainers only)
Add new tool: modify `Executor.ExecuteAsync`, implement handler, keep name lowercase, and document args here. Maintain backward compatibility with existing names.

### 9. Example Turn
{
  "steps": [
    {"tool":"hotkey","args":{"keys":["win","r"]},"human_readable_justification":"Open Run"},
    {"tool":"write","args":{"text":"notepad"},"human_readable_justification":"Type app name"},
    {"tool":"press","args":{"key":"enter"},"human_readable_justification":"Launch"}
  ],
  "done": null
}

### 10. DO / AVOID Quick List
DO: Minimal valid JSON, reuse coordinates, short justifications, finish early with `done` when goal reached.
AVOID: Markdown fences, invented tool names, speculative multi-step chains, long sleeps, verbose prose.

Feedback: Open an issue or comment in PR if parsing errors recur or new tool semantics needed.
