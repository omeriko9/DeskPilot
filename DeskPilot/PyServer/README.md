# DeskPilot Remote LLM Proxy (Python)

A minimal FastAPI server that mimics the behavior expected by `RemoteLLMClient` in the C# application.
It receives a POST with the fields:

```
{
  "systemPrompt": string,
  "originalUserRequest": string,
  "originalUserRequestBase64": string,
  "userContextJson": string,
  "screenshotPngBase64": string
}
```

It constructs an OpenAI "responses" endpoint payload identical to the in-process `OpenAIClient` and forwards it.
The raw JSON returned by OpenAI is relayed back so the existing `OpenAIResponseParser` can extract text.

## Environment Variables

- `OPENAI_API_KEY` (required for real calls; if absent a mock JSON is returned)
- `OPENAI_BASE_URL` (default: https://api.openai.com/v1)
- `OPENAI_MODEL` (default: gpt-4.1-mini)
- `OPENAI_TIMEOUT` (default: 60 seconds)
- `PORT` (default: 8000)

## Install & Run

```powershell
python -m venv venv
venv\Scripts\Activate.ps1
pip install fastapi uvicorn httpx
$env:OPENAI_API_KEY="sk-..."
python PyServer/server.py
```

Then point the C# `RemoteLLMClient` to `http://localhost:8000/`.

Health check:
```
GET http://localhost:8000/health
```

## Mock Mode
If `OPENAI_API_KEY` is not set, the server returns a mock response shaped like:
```
{"output":[{"content":[{"text":"{\"mock\": true, ...}"}]}]}
```
The parser will extract the inner JSON string.

## Notes
- No streaming implemented (matches current non-streaming C# client behavior).
- If you later add Azure/OpenAI variants just adjust base URL & auth header logic.
