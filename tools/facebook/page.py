from typing import Dict, Any, List, Optional
import os
from client import GraphAPIClient

class FacebookPageManager:
    def __init__(self, page_id: str, page_access_token: str):
        self.page_id = page_id
        self.client = GraphAPIClient(access_token=page_access_token)

    def get_page_info(self) -> Dict[str, Any]:
        fields = "id,name,fan_count,followers_count,category,link,verification_status"
        return self.client.get(f"/{self.page_id}", params={"fields": fields})

    def publish_text_post(
        self,
        message: str,
        link: Optional[str] = None,
        scheduled_publish_time: Optional[int] = None
    ) -> Dict[str, Any]:
        payload = {"message": message}
        if link:
            payload["link"] = link

        if scheduled_publish_time:
            payload["published"] = "false"
            payload["scheduled_publish_time"] = str(scheduled_publish_time)

        return self.client.post(f"/{self.page_id}/feed", data=payload)

    def upload_photo(self, photo_path_or_url: str, caption: str = "") -> Dict[str, Any]:
        if photo_path_or_url.startswith(("http://", "https://")):
            return self.client.post(
                f"/{self.page_id}/photos",
                data={"url": photo_path_or_url, "caption": caption}
            )
        else:
            with open(photo_path_or_url, "rb") as f:
                return self.client.post(
                    f"/{self.page_id}/photos",
                    data={"caption": caption},
                    files={"source": f}
                )

    def get_page_posts(self, limit: int = 25) -> List[Dict[str, Any]]:
        fields = "id,message,created_time,shares,comments.summary(true),reactions.summary(true)"
        res = self.client.get(f"/{self.page_id}/posts", params={"fields": fields, "limit": limit})
        return res.get("data", [])

    def reply_to_comment(self, comment_id: str, message: str) -> Dict[str, Any]:
        return self.client.post(f"/{comment_id}/comments", data={"message": message})

    def hide_comment(self, comment_id: str, hide: bool = True) -> Dict[str, Any]:
        return self.client.post(f"/{comment_id}", data={"is_hidden": str(hide).lower()})

    def get_page_analytics(self) -> Dict[str, Any]:
        metrics = [
            "page_impressions_unique",
            "page_post_engagements",
            "page_views_total",
            "page_fan_adds"
        ]
        params = {"metric": ",".join(metrics), "period": "day"}
        return self.client.get(f"/{self.page_id}/insights", params=params)
