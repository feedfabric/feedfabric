# FeedFabric for Python

Call FeedFabric from your servers, for your members, with an API key. It works on Python 3.9+ and has no
dependencies. It is fully typed (`py.typed`, mypy strict). [../README.md](../README.md) covers API keys, tags and
typed data for every SDK.

```sh
pip install feedfabric
```

```python
import os
from feedfabric import FeedFabric, FeedFabricError

feed = FeedFabric("https://feed.example.com", os.environ["FEEDFABRIC_API_KEY"])
member = feed.as_member(user.id, username=user.handle, display_name=user.name)

page = member.feeds.for_you(limit=20)
member.posts.like(page["posts"][0]["id"])
```

Responses are the API's JSON as dicts, typed with `TypedDict`s (`feedfabric.types`), so mypy and editors know their
keys.

## Typed data

```python
from dataclasses import asdict, dataclass
from typing import Optional
from feedfabric import DataType

@dataclass
class WeightReading:
    kg: float
    note: Optional[str] = None

Weight = DataType[WeightReading](
    "weight-reading",
    parse=lambda v: WeightReading(**v),
    dump=lambda r: {k: v for k, v in asdict(r).items() if v is not None},
)

member.posts.create(tags=["weight"], data=Weight.of(WeightReading(kg=82.4)))

for post in member.posts.list(type=Weight.key)["posts"]:
    reading = Weight.read(post)  # WeightReading | None
```

Without `parse` and `dump`, `DataType[MyTypedDict]("…")` passes the JSON through as that `TypedDict`. Pydantic
models work too: `parse=Reading.model_validate, dump=lambda r: r.model_dump(exclude_none=True)`. To generate the
model from the registered schema, use `datamodel-code-generator --input schema.json --input-file-type jsonschema`,
with the schema from `GET /data-types/weight-reading/schema`.

## Errors

```python
try:
    member.posts.create(content)
except FeedFabricError as e:
    if e.code != "content.violation":
        raise
    show_rules(e.params.get("category"))
```

## Tests

`uv run pytest` and `uv run mypy`.
