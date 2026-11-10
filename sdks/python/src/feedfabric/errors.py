from __future__ import annotations

from typing import Any, Dict, List, Optional


class FeedFabricError(Exception):
    """An API error. Every one has a stable code (docs/errors.md): match on code, not on the message."""

    def __init__(
        self,
        status: int,
        code: str,
        message: str,
        params: Optional[Dict[str, Any]] = None,
        errors: Optional[List[Any]] = None,
        trace_id: Optional[str] = None,
        retry_after: Optional[float] = None,
    ) -> None:
        super().__init__(message)
        # The HTTP status; 0 when the request never got a response
        self.status = status
        # e.g. "post.not_found", "content.violation", "rate_limited"; "network_error" without a response
        self.code = code
        self.message = message
        self.params: Dict[str, Any] = params or {}
        # validation_failed lists fields; post.data_invalid lists {"path", "message"}
        self.errors: List[Any] = errors or []
        self.trace_id = trace_id
        # Seconds to wait, from Retry-After on 429 and 503
        self.retry_after = retry_after

    def __repr__(self) -> str:
        return f"FeedFabricError(status={self.status}, code={self.code!r}, message={self.message!r})"
