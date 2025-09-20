import os
import base64
import logging
from typing import Optional

from fastapi import FastAPI, HTTPException, Request
from pydantic import BaseModel
import httpx

# Minimal Python server that mimics the behavior expected by RemoteLLMClient.
# It accepts the payload fields the C# client sends and forwards them to OpenAI (responses endpoint)
# returning a JSON body whose shape the existing OpenAIResponseParser can consume.

OPENAI_API_KEY = os.getenv("OPENAI_API_KEY", "")
OPENAI_BASE_URL = os.getenv("OPENAI_BASE_URL", "https://api.openai.com/v1")
OPENAI_MODEL = os.getenv("OPENAI_MODEL", "gpt-4.1-mini")  # adjust model id as needed
TIMEOUT = float(os.getenv("OPENAI_TIMEOUT", "60"))
PORT_NUMBER = "8009"

logger = logging.getLogger("pyserver")
logging.basicConfig(level=logging.INFO)

app = FastAPI(title="DeskPilot Remote LLM Proxy")

class IncomingRequest(BaseModel):
    systemPrompt: str
    originalUserRequest: str
    originalUserRequestBase64: str
    userContextJson: str
    screenshotPngBase64: str

# Build OpenAI "responses" endpoint payload consistent with C# OpenAIClient.

def build_openai_payload(data: IncomingRequest):
    return {
        "model": OPENAI_MODEL,
        "input": [
            {
                "role": "system",
                "content": [
                    {"type": "input_text", "text": data.systemPrompt}
                ],
            },
            {
                "role": "user",
                "content": [
                    {"type": "input_text", "text": "ORIGINAL_USER_REQUEST_UTF8:\n" + data.originalUserRequest},
                    {"type": "input_text", "text": "ORIGINAL_USER_REQUEST_BASE64:\n" + data.originalUserRequestBase64},
                    {"type": "input_text", "text": "USER_CONTEXT_JSON:\n" + data.userContextJson + "\n\nSTRICTLY FOLLOW THE SYSTEM RULES. Return ONLY the raw JSON object."},
                    {"type": "input_image", "image_url": f"data:image/png;base64,{data.screenshotPngBase64}"},
                ],
            },
        ],
    }

async def call_openai(payload: dict) -> dict:
    if not OPENAI_API_KEY:
        # Return mock response when key is missing (for offline dev)
        mock_text = '{"mock": true, "reason": "OPENAI_API_KEY missing"}'
        logger.info("Mock mode: returning mock response")
        return {"output": [{"content": [{"text": mock_text}]}]}

    url = f"{OPENAI_BASE_URL.rstrip('/')}/responses"
    headers = {
        "Authorization": f"Bearer {OPENAI_API_KEY}",
        "Content-Type": "application/json",
    }
    logger.info(f"Calling OpenAI at {url} with payload size: {len(str(payload))} bytes")
    async with httpx.AsyncClient(timeout=TIMEOUT) as client:
        resp = await client.post(url, json=payload, headers=headers)
        logger.info(f"OpenAI response status: {resp.status_code}, size: {len(resp.text)} bytes")
        if resp.status_code >= 400:
            raise HTTPException(status_code=resp.status_code, detail=resp.text)
        return resp.json()

@app.post("/")
async def root(request: IncomingRequest, req: Request):
    client_ip = req.client.host if req.client else "unknown"
    request_size = len(str(request.dict()))  # approximate size
    logger.info(f"Client {client_ip} connected, request size: {request_size} bytes")
    payload = build_openai_payload(request)
    try:
        openai_json = await call_openai(payload)
        response_size = len(str(openai_json))
        logger.info(f"Sending response to {client_ip}, size: {response_size} bytes")
    except HTTPException:
        raise
    except Exception as ex:
        logger.exception("Unhandled error calling OpenAI")
        raise HTTPException(status_code=500, detail=str(ex))

    # Pass through raw JSON; C# will parse using OpenAIResponseParser.
    return openai_json

# Optional simple health endpoint
@app.get("/health")
async def health(req: Request):
    client_ip = req.client.host if req.client else "unknown"
    logger.info(f"Health check from {client_ip}")
    return {"status": "ok"}

if __name__ == "__main__":
    import uvicorn
    port = int(os.getenv("PORT", PORT_NUMBER))
    logger.info(f"Starting server on port {port}")
    uvicorn.run("server:app", host="0.0.0.0", port=port, reload=False)
