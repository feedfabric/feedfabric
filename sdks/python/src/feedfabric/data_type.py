from __future__ import annotations

from typing import Any, Callable, Generic, Mapping, Optional, TypeVar, cast

from .types import PostDataInput

T = TypeVar("T")


class DataType(Generic[T]):
    """One of your data types, typed. The API checks data against the type's JSON Schema; this makes the same promise
    in your code, so a weight reading can't be posted as anything else and reading one back gives your own type.

        class WeightReading(TypedDict):
            kg: float

        Weight = DataType[WeightReading]("weight-reading")
        member.posts.create(tags=["weight"], data=Weight.of({"kg": 82.4}))
        reading = Weight.read(post)  # WeightReading | None

    parse checks or converts the raw value as it's read: a pydantic model's model_validate, a dataclass's constructor
    (lambda v: Reading(**v)), or your own function. dump turns your type back into JSON-ready values when posting,
    for types that aren't already dicts (dataclasses.asdict, model_dump).
    """

    def __init__(
        self,
        key: str,
        parse: Optional[Callable[[Any], T]] = None,
        dump: Optional[Callable[[T], Any]] = None,
    ) -> None:
        self.key = key
        self._parse = parse
        self._dump = dump

    def of(self, value: T) -> PostDataInput:
        """The post's data, for posts.create and posts.edit."""
        return {"type": self.key, "value": self._dump(value) if self._dump else value}

    def is_type(self, post: Mapping[str, Any]) -> bool:
        """Whether a post carries this type of data."""
        data = post.get("data")
        return isinstance(data, Mapping) and data.get("type") == self.key

    def read(self, post: Mapping[str, Any]) -> Optional[T]:
        """The post's data as a T, or None when it has none of this type."""
        if not self.is_type(post):
            return None
        value = post["data"]["value"]
        return self._parse(value) if self._parse else cast(T, value)
