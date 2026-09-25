from typing import Dict, Any, List
from client import GraphAPIClient
from config import settings

class FacebookAuthManager:
    def __init__(self, user_token: str):
        self.client = GraphAPIClient(access_token=user_token)

    def get_long_lived_user_token(self) -> Dict[str, Any]:
        params = {
            "grant_type": "fb_exchange_token",
            "client_id": settings.app_id,
            "client_secret": settings.app_secret,
            "fb_exchange_token": self.client.access_token,
        }
        return self.client.get("/oauth/access_token", params=params)

    def debug_token(self, input_token: str) -> Dict[str, Any]:
        app_token = f"{settings.app_id}|{settings.app_secret}"
        debug_client = GraphAPIClient(access_token=app_token)
        return debug_client.get("/debug_token", params={"input_token": input_token})

    def get_user_pages(self) -> List[Dict[str, Any]]:
        res = self.client.get("/me/accounts", params={"fields": "id,name,access_token,category,tasks"})
        return res.get("data", [])
