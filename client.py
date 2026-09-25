import logging
import json
import time
import httpx
from typing import Any, Dict, Optional
from tenacity import retry, stop_after_attempt, wait_exponential, retry_if_exception_type

from config import settings
from exceptions import FacebookAPIError, FacebookAuthError, FacebookRateLimitError

logger = logging.getLogger("FacebookClient")

class GraphAPIClient:
    def __init__(self, access_token: str, api_version: Optional[str] = None):
        self.access_token = access_token
        self.api_version = api_version or settings.api_version
        self.base_url = f"{settings.graph_base_url}/{self.api_version}"
        self.client = httpx.Client(timeout=30.0)

    def _parse_rate_limits(self, headers: httpx.Headers):
        app_usage = headers.get("x-app-usage")
        page_usage = headers.get("x-page-usage")

        if app_usage:
            usage = json.loads(app_usage)
            max_pct = max(usage.values())
            if max_pct > 80:
                logger.warning(f"App usage reached {max_pct}%. Throttling requests...")
                time.sleep(2.0)

        if page_usage:
            usage = json.loads(page_usage)
            max_pct = max(usage.values())
            if max_pct > 80:
                logger.warning(f"Page usage reached {max_pct}%. Throttling requests...")
                time.sleep(2.0)

    def _handle_response(self, response: httpx.Response) -> Dict[str, Any]:
        self._parse_rate_limits(response.headers)
        data = response.json()

        if "error" in data:
            err = data["error"]
            code = err.get("code")
            subcode = err.get("error_subcode")
            msg = err.get("message", "Unknown Graph API Error")
            type_ = err.get("type")

            if code in (190, 102) or subcode in (458, 460, 463, 467):
                raise FacebookAuthError(msg, code=code, subcode=subcode, type_=type_)
            if code in (4, 17, 32, 613):
                raise FacebookRateLimitError(msg, code=code, subcode=subcode, type_=type_)

            raise FacebookAPIError(msg, code=code, subcode=subcode, type_=type_)

        return data

    @retry(
        retry=retry_if_exception_type((httpx.TransportError, FacebookRateLimitError)),
        stop=stop_after_attempt(3),
        wait=wait_exponential(multiplier=1, min=2, max=10)
    )
    def request(
        self,
        method: str,
        endpoint: str,
        params: Optional[Dict[str, Any]] = None,
        data: Optional[Dict[str, Any]] = None,
        files: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        url = f"{self.base_url}/{endpoint.lstrip('/')}"
        params = params or {}
        params["access_token"] = self.access_token

        response = self.client.request(
            method=method,
            url=url,
            params=params,
            data=data,
            files=files
        )
        return self._handle_response(response)

    def get(self, endpoint: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        return self.request("GET", endpoint, params=params)

    def post(
        self,
        endpoint: str,
        data: Optional[Dict[str, Any]] = None,
        params: Optional[Dict[str, Any]] = None,
        files: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        return self.request("POST", endpoint, params=params, data=data, files=files)

    def delete(self, endpoint: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        return self.request("DELETE", endpoint, params=params)
