---
layout: ../../layouts/Doc.astro
title: For You ranking
description: How ForYouService turns candidates from four sources into a ranked, varied feed, and how sessions and cursors keep paging stable.
---

```http
GET /userfeed/foryou?limit=20
GET /userfeed/foryou?limit=20&cursor=20
GET /userfeed/foryou?limit=20&profile=discovery
```

Every step below is set by a [ranking profile](/how-it-works/profiles/); the numbers here are the defaults of the
`balanced` preset, which serves any request that doesn't name one (`Ranking:DefaultProfile`).

## 1. Candidates

`ForYouService` gathers post IDs from up to four sources, each switchable and capped:

| Source | What | Default cap |
|---|---|---|
| **Following** | The newest entries of the member's Redis timeline | 300 |
| **Topics** | Posts from the last 6 hours on the member's top 3 topics, most liked first | 100 |
| **Similar readers** | Posts liked lately by the 100 members who share most of the member's last 50 likes | 100 |
| **Similar meaning** | Posts from the last day closest in meaning to what the member likes (MiniLM embeddings, searched in memory) | 100 |

Then, whatever the sources say:

- the member's **favourites'** live posts from the last 48 hours (up to 100) are added;
- a repost stands for the post it shares;
- posts the member pressed **Not interested** on in the last 30 days are removed;
- only live originals stay: not deleted, visible, not the member's own, by an active account, and younger than the
  profile's maximum age if it has one.

Topic and affinity scores come from Redis sorted sets the engagement worker keeps up to date from likes and comments,
and that *Not interested* / *Show more* move directly.

## 2. Signals

`FeatureBuilder` works out the same 20 signals for every candidate, in a handful of batched queries. They are the
model's inputs, the formula's inputs, the variables a custom formula can use, and what the feed log stores.

| Signal | Meaning |
|---|---|
| `age_minutes` | How old the post is |
| `follows_author`, `author_affinity` | Whether the viewer follows the author; points from their likes and comments of the author's posts |
| `topic_match`, `session_topic_match` | Share of what the viewer engages with (overall, and in the last 15 minutes) that's on this post's topics |
| `semantic_match` | Cosine similarity between the post and the average of the viewer's recent likes |
| `likes`, `comments`, `velocity_30m` | Counts, and likes plus comments in the last 30 minutes |
| `impressions`, `opens`, `ctr`, `engagement_rate`, `avg_dwell_s` | How everyone who saw it reacted: from **your clients' impressions, opens and dwell** |
| `viewer_author_ctr`, `viewer_author_seen` | How often this viewer opened the author's posts after seeing them |
| `author_followers_log`, `has_media`, `content_length` | About the author and the post |
| `position` | Where it was served (0 at the top); always 0 when scoring |

Rates are smoothed, so a post nobody has seen yet is treated as roughly average rather than zero.

<div class="why">

**Seven of the twenty signals exist only because front ends report reading.** Impressions, dwell and opens
(`POST /userfeed/impressions`, `/dwell`, `/posts/{id}/open`) feed `impressions`, `opens`, `ctr`, `engagement_rate`,
`avg_dwell_s` and the viewer-author pair, and decide which posts count as "seen already". See
[Reading signals](/build-a-ui/signals/).

</div>

## 3. Filters

The profile's filters drop candidates outright: excluded tags, *only these tags*, photos only, a minimum number of
likes. Then every remaining post is scored.

## 4. Scoring

The profile's scoring mode decides the score; the response's `ranker` says what was used.

| Mode | Score | `ranker` |
|---|---|---|
| `model` (default) | The trained model's value with the profile's **goals** (below), or the formula until a model is loaded | `model:<version>` or `heuristic` |
| `formula` | `HeuristicRanker`, the built-in formula | `heuristic` |
| `expression` | The profile's own formula, which can use `model` and `formula` as variables | `expression` or `expression+model:<version>` |
| `newest` | Newest first, no ranking | `newest` |

**The model** is five LightGBM classifiers, one per outcome, run in C# with ONNX Runtime. A post's value is

```
value = 0.5·P(open) + 1·P(like) + 3·P(comment) + 2·P(long read ≥ 10 s) − 1.5·P(skip)
```

with the weights taken from the profile's goals. `RankerModel` reads `models/ranker/latest.json` from blob storage,
checks for a newer one at most once a minute, and refuses a model trained on a different list of signals.

**The formula** is recency × interest × quality, each part at least 1 so none can zero a post out: recency halves
about every 2¾ hours; interest grows with topic match, meaning, this session, the author and following them;
quality with engagement and open rates, time on screen and how fast it's picking up likes now.

## 5. Adjustments

On top of the score, each multiplying it and each listed as a reason:

- **Freshness**: an extra half-life, if the profile sets one.
- **Boosts**: by tag, author, photos, following or not, or favourite (`balanced` lifts favourites ×1.5).
- **Seen already**: a post the member saw in the last 24 hours keeps 35% of its score. "Seen" means an impression
  was reported, or the member opened or engaged with it.

## 6. Variety and exploration

`DiversityReranker` turns the scored list into a feed, ten posts at a time:

- at most 2 posts by one author in any ten;
- no more than 2 posts on the same main topic in a row;
- at least 2 posts from accounts the member doesn't follow in each ten, when there are any;
- **1 exploration slot** in each ten (slot 6, counting from 0; two slots use 3 and 7, three use 2, 5 and 8), filled
  with a random post from below rank 30 that still fits the rules.

Each rule gives way when nothing left can meet it, so the feed never comes up short. Explored posts are marked
`explored: true` and logged with the chance they had of being picked: they're the only training data not shaped by
the ranker's own past choices. The random choice is seeded per member and minute, so refreshing within a minute
explores the same posts.

## 7. Sessions and cursors

Ranking a few hundred candidates takes real work, and reshuffling while someone scrolls would show them the same
posts twice. So:

- **The first page (no `cursor`) always ranks afresh** and saves the whole ordered list in Redis as the member's
  session for that profile version, for **15 minutes**.
- **Later pages read the saved order.** `nextCursor` is an offset (`"20"`, `"40"`…), not a post ID; pass it back
  unchanged. It's `null` at the end of the list.
- Each page is checked again as it's served: a post deleted, held or whose author was suspended since the first page
  is left out, so **a page can be shorter than `limit`** even when `nextCursor` isn't null.
- If the session has expired by the time a later page is asked for, the API ranks again and serves from the same
  offset of the new list. **Drop posts you've already shown** (by `id`) when appending a page.
- Sessions are per profile version (`foryou:{member}:{profile}`), so switching profiles never pages through another
  profile's order.

```json
{
  "posts": [
    {
      "id": "…", "authorUsername": "maya_runs", "content": "…", "tags": [ … ], "media": [ … ],
      "ranking": {
        "ranker": "model:20261105-041807", "position": 0, "score": 1.8412, "explored": false,
        "factors": [ { "label": "Your topics", "value": 2.31, "detail": "44% match with what you engage with" }, … ],
        "features": { "age_minutes": 37.2, "follows_author": 1, … }
      }
    }
  ],
  "nextCursor": "20",
  "ranker": "model:20261105-041807",
  "profile": "balanced",
  "candidates": 412
}
```

`profile` is `<key>` for a preset or `<key>@<version>` for a saved profile; `candidates` is how many posts were
ranked for this session. `ranking.factors` are the reasons in words, for a [*Why this post?*](/build-a-ui/post-cards/#why-this-post)
panel; `ranking.features` are the raw signals.

## Not interested and Show more

```http
POST /userfeed/posts/{postId}/not-interested
POST /userfeed/posts/{postId}/show-more
```

Both return `204`. They're a **state**, not a counter: pressing the same one again changes nothing, and switching
from one to the other first undoes the earlier one.

- **Not interested** hides the post from For You for 30 days and counts its author (−3 affinity) and topics
  (−2 × the tag's confidence) against similar posts for this member from now on. It works on any post the member was
  shown, even one held or removed since.
- **Show more** does the opposite with the same weight (more than a like), and lets a hidden post come back. It only
  works on posts everyone can see.

Both label the post's feed log rows, so the ranker learns from them too. The current session isn't re-ranked: remove
a *Not interested* post from the screen yourself.

## Previewing

Staff with the `ranking-admin` role can see what any settings would give any member, without saving or logging
anything: `POST /ranking/preview?viewer=<externalUserId>&limit=30` with a profile in the body. The Feed studio in the
admin panel is built on it. See [Ranking profiles](/how-it-works/profiles/).
