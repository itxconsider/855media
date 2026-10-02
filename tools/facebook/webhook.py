import hmac
import hashlib
from fastapi import FastAPI, Request, HTTPException, Query, Response
from config import settings

app = FastAPI(title="Facebook Webhook Listener")

@app.get("/webhook")
async def verify_webhook(
    mode: str = Query(..., alias="hub.mode"),
    token: str = Query(..., alias="hub.verify_token"),
    challenge: str = Query(..., alias="hub.challenge")
):
    if mode == "subscribe" and token == settings.webhook_verify_token:
        return Response(content=challenge, media_type="text/plain")
    raise HTTPException(status_code=403, detail="Verification token mismatch")

@app.post("/webhook")
async def handle_webhook_events(request: Request):
    signature = request.headers.get("X-Hub-Signature-256")
    if not signature:
        raise HTTPException(status_code=400, detail="Missing X-Hub-Signature-256 header")

    body = await request.body()
    expected_hash = hmac.new(
        key=settings.app_secret.encode("utf-8"),
        msg=body,
        digestmod=hashlib.sha256
    ).hexdigest()
    
    actual_hash = signature.replace("sha256=", "")
    if not hmac.compare_digest(expected_hash, actual_hash):
        raise HTTPException(status_code=403, detail="Invalid HMAC signature")

    payload = await request.json()
    for entry in payload.get("entry", []):
        page_id = entry.get("id")
        for change in entry.get("changes", []):
            field = change.get("field")
            value = change.get("value")
            print(f"Received update on Page [{page_id}] -> Field: {field}, Value: {value}")

    return {"status": "EVENT_RECEIVED"}
