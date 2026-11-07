---
layout: ../../layouts/Doc.astro
title: Feed log and training
description: Every post served is logged with the signals it was ranked on; what the reader does next labels it; ml/train_ranker.py learns from the labels and the API picks up the model within a minute.
---

## The loop

<ol class="flow">
  <li><strong>Serve</strong>A Following, favourites or For You page is served and each post written to <code>FeedLog</code> with its signals and position.</li>
  <li><strong>Read</strong>The front end reports impressions, dwell and opens; likes, comments and <em>Not interested</em> come through their own endpoints.</li>
  <li><strong>Label</strong>Each report marks the viewer's rows for that post served in the last hour.</li>
  <li><strong>Train</strong><code>make train</code> fits one model per outcome on the last 7 days and checks it against the formula.</li>
  <li><strong>Publish</strong>ONNX files and <code>latest.json</code> go to blob storage.</li>
  <li><strong>Rank</strong>The API loads the new model within a minute; For You's <code>ranker</code> says <code>model:&lt;version&gt;</code>.</li>
</ol>

## The feed log

`FeedLog` (Postgres) has one row per post served to one viewer in one feed:

| Column | What |
|---|---|
| `ViewerId`, `PostId`, `Feed` | Who, what, and `following`, `foryou` or `favourites` |
| `Ranker`, `Profile` | What ordered it (`chronological`, `heuristic`, `model:<version>`…) and the profile as `key` or `key@version` |
| `Position`, `Score`, `Features` | Where it was shown (0 = top), its score, and the 20 signals as JSON, with the real position |
| `ServedAt` | When |
| `Seen`, `Opened`, `Liked`, `Commented`, `DwellMs`, `NotInterested`, `ShowMore` | The labels, filled in as the reader acts |
| `Explored`, `Propensity` | Whether it filled an exploration slot, and the chance it had of being picked |

- A post served to the same viewer in the same feed **within 30 minutes isn't logged again**, so refreshing doesn't
  flood the log.
- An action labels the viewer's rows for that post **served in the last hour** (the attribution window).
- Opening, liking, commenting, *Not interested* and *Show more* all imply `Seen`. *Not interested* and *Show more*
  cancel each other: the latest press wins.
- Dwell adds up across reports.
- Logging the signals as served keeps training and serving on the same numbers.

## What the model learns

`ml/train_ranker.py` reads the rows marked **`Seen`** from the last 7 days (`--days`) and refuses to train on fewer
than 2,000 (`--min-rows`). A post served but never reported as seen says nothing either way, so it's left out. Each row
gets five labels, one LightGBM classifier each (a "head"):

| Head | Label | Weight in the value |
|---|---|---|
| `open` | Opened | 0.5 |
| `like` | Liked | 1 |
| `comment` | Commented | 3 |
| `long_dwell` | On screen for 10 seconds or more in total | 2 |
| `skip` | *Not interested*, or on screen for under a second without being opened, liked or commented on | −1.5 |

- **Position** is an input, so the model learns how much the top spot itself adds; the API scores every post at
  position 0 so a post isn't rewarded for having been near the top before.
- **Exploration rows count three times** (`EXPLORED_WEIGHT`): they're the only data not shaped by the ranker's own
  choices.
- **Evaluation** is on the newest 20% of rows (a time split, as the model will be used on the future): AUC for each
  head, and NDCG@10 and AUC of the combined value against the formula, per feed session.
- **Export:** each head to ONNX, checked against LightGBM's own predictions, written as
  `models/ranker/<version>-<head>.onnx`, then `models/ranker/latest.json` last, so the API never points at a model
  that isn't there yet. `latest.json` lists the heads, the weights, the metrics, signal importances, and the source.

## Whose behaviour

`--source` picks the rows:

| Source | Rows from | Use |
|---|---|---|
| `real` (default) | Members whose external ID doesn't start with `sim-` | Production |
| `synthetic` | The simulator's `sim-*` members | Experiments |
| `all` | Everyone | Development (`make train`) |

A model trained on synthetic data is refused outside Development (unless `Ranking:AllowSimulatedRanker` is on), so
real members never get one: For You ranks by its formula instead. The trainer also writes each file's SHA-256 to
`latest.json`, and a file that doesn't match is refused. The feed log itself is kept for 180 days
(`Retention:FeedLog`).

<div class="warn">

**Don't give real members IDs starting with `sim-`.** The trainer tells simulated members from real ones by that
prefix on `ExternalUserId`, the ID your identity provider puts in the token.

</div>

## Publishing elsewhere

```bash
cd ml
uv run train_ranker.py                            # to ../.blobs (local blob storage)
uv run train_ranker.py --s3-bucket my-bucket      # to S3, where a server's API reads it
uv run train_ranker.py --s3-endpoint http://localhost:4566 --s3-bucket feedfabric-media   # LocalStack
DATABASE_URL=postgresql://… uv run train_ranker.py
```

The API's `RankerModel` checks `models/ranker/latest.json` at most once a minute. A model whose signal list differs
from the API's `RankingFeatures.Names` is refused and the formula is used; `ml/train_ranker.py` keeps its own copy of
that list (`FEATURES`), so change both together. The same goes for the formula, which the trainer re-implements to
compare against.

## Why front ends matter here

Every label above except likes and comments comes from what the client reports. A front end that never sends
impressions produces no `Seen` rows, so **nothing it serves becomes training data**; one that never sends dwell
produces no long reads or skips. [Reading signals](/build-a-ui/signals/) shows how to report them correctly.
