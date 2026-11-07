---
layout: ../../layouts/Doc.astro
title: Formula cookbook
description: Ready-to-use formulas for common feeds, when a profile setting beats a formula, the mistakes that make formulas misbehave, and how to try one safely before members see it.
---

Each recipe is a complete `scoring` block for a [ranking profile](/how-it-works/profiles/). Paste it into the Feed
studio, or send it in a profile to `POST /ranking/preview` or `PUT /ranking/profiles/{key}`.
[How a score is made](/formulas/) explains `formula` and `model`, and [Formula language](/formulas/language/) every
variable and function.

## Before you write a formula

Many changes don't need one. Profile settings are simpler, validated, and shown to members as reasons in plain
words:

| You want | Use |
|---|---|
| Value comments, opens or long reads more than likes | `scoring.goals` with `mode: "model"` |
| Newer posts to win more often | `freshness.halfLifeHours` |
| Nothing older than two days | `freshness.maxAgeHours: 48` |
| More from favourites, followed accounts, a tag, an author or photo posts | `boosts` |
| Only posts tagged `#running`; never `#politics`; only photos; at least 5 likes | `filters` |
| Fewer posts in a row from one author or topic | `variety` |

Write a formula when the score should depend on **how strongly** something is true (more comments, a closer topic
match), combine signals in a way the settings can't (`if` this and that), or use a signal no setting reads
(`content_length`, `author_followers_log`, `velocity_30m`).

## Recipes

### The model, fading more slowly

```json
{ "mode": "expression", "expression": "model * decay_exp(age_hours, 12)" }
```

The trained model decides, and a post loses half its score every 12 hours on top of what the model already knows
about age. A good default for a slower community where good posts stay relevant for a day.

### Popular now

```json
{ "mode": "expression", "expression": "log1p(likes + 2 * comments) * decay_exp(age_hours, 6)" }
```

Engagement, with comments counting double, halved every six hours. `log1p` keeps a post with 400 likes from
burying everything else: it scores about 6 against 3.4 for a post with 30.

### Conversations

```json
{ "mode": "expression", "expression": "formula * (1 + log1p(comments))" }
```

The built-in formula, lifted by the number of comments: ×1.7 for one, ×3.4 for ten. For a model-based version, use
goals instead: `{ "mode": "model", "goals": { "comment": 5, "open": 0.3 } }`, close to the `community` preset.

### Mostly your topics

```json
{ "mode": "expression", "expression": "if(topic_match > 0.2, formula, formula * 0.2)" }
```

Posts on the member's topics keep their score; others keep a fifth of it. Unlike `filters.onlyTags`, off-topic
posts can still turn up when there isn't enough on-topic, and it works per member rather than for one fixed tag.

### Trending

```json
{ "mode": "expression", "expression": "formula * (1 + 0.3 * log1p(velocity_30m)) * decay_exp(age_hours, 3)" }
```

Picking up speed in the last half hour, and new. Pair it with `freshness.maxAgeHours: 48` and a short
`sources.lookbackHours` (the `fresh` preset uses 6) so the candidates are recent too.

### People you actually talk to

```json
{ "mode": "expression", "expression": "formula * (1 + 0.5 * log1p(author_affinity)) * if(follows_author, 1.3, 1)" }
```

The built-in formula already likes familiar authors; this doubles down, for a "friends" tab. `author_affinity` goes
up with each like (1) and comment (2) of the author's posts, so ten interactions give ×2.2 before the follow bonus.

### New voices

```json
{ "mode": "expression", "expression": "formula * (2 - min(author_followers_log, 7) / 7)" }
```

Up to ×2 for accounts with no followers, falling to ×1 at about 1,100 followers (`ln 1100 ≈ 7`) and staying there.
Combine it with a higher `variety.outOfNetwork` so the feed actually includes people the member doesn't follow.

### Long reads

```json
{ "mode": "expression", "expression": "formula * (1 + 0.4 * log1p(content_length / 280)) * if(avg_dwell_s > 8, 1.5, 1)" }
```

Longer posts, and posts people stay on. `content_length / 280` makes a tweet-length post count as 1.

### Quietly demote what people pass over

```json
{ "mode": "expression", "expression": "if(impressions > 50 and ctr < 0.02, formula * 0.3, formula)" }
```

Once a post has been seen 50 times and almost nobody opens it, it keeps 30% of its score. The guard on
`impressions` matters: `ctr` is smoothed, so new posts sit at 0.05 and aren't caught.

### Blending the model and the formula

```json
{ "mode": "expression", "expression": "model * sqrt(formula)" }
```

The model's judgement, nudged by the built-in formula's view of topics and freshness. Multiplying works where adding
doesn't, because the two are on different scales (see below).

## Mistakes that make formulas misbehave

**Adding scores on different scales.** `0.7 * model + 0.3 * formula` looks like a 70/30 blend, but `model` is usually
between 0 and about 5 while `formula` runs from below 1 to 20 or more, so the formula decides almost everything.
Multiply them, or bring one onto the other's scale first (`log1p(formula)`).

**Decaying twice.** `formula` already halves about every 2¾ hours. `formula * decay_exp(age_hours, 3)` is a lot
steeper than either on its own: that's intended in the `fresh` preset, but surprising elsewhere. The same goes for
setting `freshness.halfLifeHours` on a profile whose formula already decays.

**Raw counts.** `formula * likes` hands the feed to whatever is already most popular, and scores every post with no
likes 0. Use `log1p(likes)`, and add 1 (`1 + log1p(likes)`) when it multiplies.

**Zeroing posts.** Anything multiplied by a 0 condition scores 0, and posts that tie are in no useful order. Lower
them instead (`if(x, formula, formula * 0.1)`), use a boost with `multiplier: 0` to put them last reliably, or a
filter to remove them.

**Ignoring the priors.** `ctr` and `engagement_rate` start at 0.05 for a post nobody has seen yet, not 0, so a new
post looks average. Guard with `impressions` when you want proven posts only.

**Leaning on reading signals your apps don't send.** If your front ends don't report impressions, dwell and opens,
`impressions`, `opens`, `ctr`, `avg_dwell_s` and the viewer-author signals are the same for every post, and formulas
built on them rank nothing. See [Reading signals](/build-a-ui/signals/).

**Forgetting the reasons.** Members see `ranking.factors` in *Why this post?* For a formula, the first reason is
*Profile formula* with your formula's text, so keep it readable: someone may well see it.

## Trying a formula safely

1. **Draft it** in the admin panel's Feed studio, or by hand as a profile. A preset's settings from
   `GET /ranking/vocabulary` are a good starting point.
2. **Preview it for real members.** `POST /ranking/preview` ranks for any member without saving, logging or touching
   their session, and returns the same response as For You:

   ```bash
   # Development: X-Dev-User acts as a user; elsewhere send a token with the ranking-admin role
   curl -s -X POST 'http://localhost:5161/ranking/preview?viewer=sim-12&limit=20' \
     -H 'X-Dev-User: 1' -H 'Content-Type: application/json' \
     -d '{ "name": "Popular now", "scoring": { "mode": "expression",
           "expression": "log1p(likes + 2 * comments) * decay_exp(age_hours, 6)" } }' \
     | jq '.posts[] | { score: .ranking.score, age: .ranking.features.age_minutes, likes: .ranking.features.likes }'
   ```

   Try several members: a new one with no history, a heavy reader, someone who follows a lot of people.
3. **Save it under its own key.** Members only get it when they ask for it (`?profile=popular-now`) or when it's
   `Ranking:DefaultProfile`.

   ```bash
   curl -s -X PUT http://localhost:5161/ranking/profiles/popular-now \
     -H 'X-Dev-User: 1' -H 'Content-Type: application/json' -d @popular-now.json
   ```

4. **Compare it on real engagement.** Each For You page is logged with `popular-now@1`; the dashboard compares
   profiles and versions on what members did with the posts they were served. Saving again makes `@2`, and
   both stay comparable.

The simulator can split its readers between profiles (`Simulator:ForYouProfiles`), which is a quick way to see how
two formulas compare before any real member uses them.
