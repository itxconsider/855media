class FacebookAPIError(Exception):
    def __init__(self, message: str, code: int = None, subcode: int = None, type_: str = None):
        super().__init__(message)
        self.message = message
        self.code = code
        self.subcode = subcode
        self.type_ = type_

    def __str__(self):
        return f"[{self.code or 'API Error'}] {self.message} (subcode: {self.subcode})"

class FacebookAuthError(FacebookAPIError):
    pass

class FacebookRateLimitError(FacebookAPIError):
    pass
