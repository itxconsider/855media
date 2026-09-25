from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict

class Settings(BaseSettings):
    app_id: str = Field(..., validation_alias="FB_APP_ID")
    app_secret: str = Field(..., validation_alias="FB_APP_SECRET")
    api_version: str = Field("v20.0", validation_alias="FB_API_VERSION")
    webhook_verify_token: str = Field(
        "super_secret_verify_token", validation_alias="FB_WEBHOOK_VERIFY_TOKEN"
    )
    graph_base_url: str = "https://graph.facebook.com"

    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8")

settings = Settings()
