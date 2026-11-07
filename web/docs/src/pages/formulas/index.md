---
layout: ../../layouts/Doc.astro
title: How a score is made
description: Every number behind a For You post's place, from the built-in formula and the trained model to your own formula, freshness, boosts and the seen-already penalty, with worked examples.
---

A For You post's place comes from one number, its **score**, worked out per member each time a first page is
ranked. This section explains that number exactly: the parts FeedFabric computes for you, the parts a
[ranking profile](/how-it-works/profiles/) sets, and the [formula language](/formulas/language/) you can use to
write your own. The code is in `src/FeedFabric.Application/Services/Ranking`.

## The pipeline

<ol class="flow">
  <li><strong>Candidates</strong>Up to four sources plus favourites, minus hidden, held, own and too-old posts.</li>
  <li><strong>Signals</strong>20 numbers per post for this member (<code>FeatureBuilder</code>).</li>
  <li><strong>Filters</strong>The profile's tag, photo and minimum-likes filters drop posts outright.</li>
  <li><strong>Base score</strong>The scoring mode: model, built-in formula, your formula, or newest.</li>
  <li><strong>Adjustments</strong>Freshness, then every matching boost, then the seen-already penalty, each a multiplier.</li>
  <li><strong>Arrange</strong>Sorted by score, then spread out for variety, with exploration slots mixed in.</li>
</ol>

Only steps 4 and 5 make the score. Steps 1 to 3 decide which posts get one, and step 6 decides where each one goes,
using the scores' **order** and nothing else: a post scoring 40 isn't treated any differently from one scoring 4.01
if both are next in line. So you never need to keep scores in a particular range; you only need the right posts to
score higher than the others.

## The signals

Every scoring mode reads the same signals, worked out for the member reading the feed. They are also the variables
a formula can use; [Formula language](/formulas/language/#variables) lists them with their ranges.

The ones that do most of the work:

| Signal | Range | Notes |
|---|---|---|
| `age_minutes` | 0 and up | `age_hours` is the same in hours, for formulas |
| `topic_match`, `session_topic_match`, `semantic_match` | 0 to 1 | How well the post fits what this member engages with, overall, in the last 15 minutes, and by meaning |
| `follows_author` | 0 or 1 | |
| `author_affinity` | 0 and up | Likes of the author's posts count 1, comments 2. *Not interested* takes 3 off, *Show more* adds 3 |
| `ctr`, `engagement_rate` | 0 to 1 | Smoothed: `(opens + 1) / (impressions + 20)` and `(likes + comments + 1) / (impressions + 20)`, so a new post starts at 0.05, not 0 |
| `viewer_author_ctr` | 0 to 1 | `(opened + 1) / (seen + 10)` for this member and this author over the last week |
| `velocity_30m` | 0 and up | Likes plus comments in the last 30 minutes |
| `avg_dwell_s` | 0 to 60 | Average seconds on screen, as your apps report it |

`impressions`, `opens`, `ctr`, `engagement_rate`, `avg_dwell_s` and the two viewer-author signals come from the
[reading signals](/build-a-ui/signals/) your front end sends. Without them these stay at their starting values for
every post, and the formula and model can only rank on topics, authors and age.

## Base score: the four modes

`scoring.mode` picks how the base score is worked out. The `ranker` on the response, and on every
[feed log](/how-it-works/training/) row, records which one actually ran.

| Mode | Base score | `ranker` |
|---|---|---|
| `model` | The trained model's value with the profile's goals, or **the built-in formula while no model is loaded** | `model:<version>`, or `heuristic` |
| `formula` | The built-in formula | `heuristic` |
| `expression` | Your formula, `scoring.expression`, which can use `formula` and `model` as variables | `expression`, or `expression+model:<version>` when it uses `model` |
| `newest` | `1 / (1 + age_minutes)`: newest first | `newest` |

### The built-in formula

`HeuristicRanker` scores every post as nine parts multiplied together. Every part except recency is **1 or more**,
so a post that's weak on one thing (no topic match, say) is never knocked to zero by it; it just doesn't get that
part's lift.

```
formula = recency × topics × meaning × session × author × follows × popular × trending × attention

recency   = e^(−age_minutes / 240)                          1 when new, half after about 2¾ hours
topics    = 1 + 3 × topic_match                             up to ×4
meaning   = 1 + 1.5 × max(0, semantic_match − 0.2)          up to ×2.2; similarity under 0.2 counts for nothing
session   = 1 + 2 × session_topic_match                     up to ×3
author    = 1 + 0.6 × ln(1 + author_affinity)               keeps growing, more and more slowly
follows   = 1 + 0.4 × follows_author                        ×1.4 if you follow them
popular   = 1 + 8 × engagement_rate + 4 × ctr               ×1.6 for a post with no views yet
trending  = 1 + 0.15 × ln(1 + velocity_30m)
attention = 1 + 0.05 × min(avg_dwell_s, 20)                 up to ×2, at 20 seconds
```

Each part, with its value and a sentence about it, is one of the post's `ranking.factors`, which is where a
[*Why this post?*](/build-a-ui/post-cards/#why-this-post) panel gets its reasons:

| Factor label | Part |
|---|---|
| Recent | recency |
| Your topics | topics |
| Similar to what you like | meaning |
| This session | session |
| Author you like | author |
| You follow them | follows |
| Popular | popular |
| Trending now | trending |
| Holds attention | attention |

**A worked example.** A post from two hours ago by someone the member follows and has liked four times, 30% on their
topics, fairly close in meaning (0.5), with 8% engagement, 12% opens, 3 likes and comments in the last half hour, and
six seconds' average dwell:

| Part | Working | Value |
|---|---|---|
| recency | e^(−120 / 240) | 0.607 |
| topics | 1 + 3 × 0.3 | 1.9 |
| meaning | 1 + 1.5 × (0.5 − 0.2) | 1.45 |
| session | 1 + 2 × 0 | 1 |
| author | 1 + 0.6 × ln 5 | 1.966 |
| follows | 1 + 0.4 | 1.4 |
| popular | 1 + 8 × 0.08 + 4 × 0.12 | 2.12 |
| trending | 1 + 0.15 × ln 4 | 1.208 |
| attention | 1 + 0.05 × 6 | 1.3 |
| **formula** | all nine multiplied | **15.31** |

The same post from someone they don't follow and have never liked scores 15.31 ÷ 1.4 ÷ 1.966 ≈ **5.56**, and a
brand-new post nobody has seen yet, on none of their topics, from a stranger, scores about **1.6**. That's the
formula's character: interest and quality multiply, and age decays everything at the same rate.

<div class="note">

**The formula is also the baseline.** `ml/train_ranker.py` has the same formula, to report how much better a trained
model ranks than it does. If you change `HeuristicRanker`, change the trainer too, or the comparison means nothing.

</div>

### The model's value

The trained ranker is five LightGBM classifiers, each predicting one thing the member might do with the post. The
profile's **goals** say how much each one is worth, and the model's value is the weighted sum:

```
model value = open × P(open) + like × P(like) + comment × P(comment) + longRead × P(long read) + skip × P(skip)
```

| Goal | Predicts | Default weight | Range |
|---|---|---|---|
| `open` | Opens the post | 0.5 | −10 to 10 |
| `like` | Likes it | 1 | −10 to 10 |
| `comment` | Comments on it | 3 | −10 to 10 |
| `longRead` | Stays on it for 10 seconds or more | 2 | −10 to 10 |
| `skip` | Scrolls past it, or presses *Not interested* | −1.5 | −10 to 10 |

With the default goals, a post with P(open) 0.3, P(like) 0.12, P(comment) 0.03, P(long read) 0.25 and P(skip) 0.4
is worth 0.15 + 0.12 + 0.09 + 0.5 − 0.6 = **0.26**. A negative weight makes the value go **below zero** for posts
the model expects to be skipped; that's fine for ordering, and the adjustments below are written to cope with it.

Changing goals changes what the feed is *for* without retraining: the `community` preset puts comments at 5 and
opens at 0.3, so it favours posts that start conversations. Until a model has been trained and published
(`make train`), `model` mode quietly uses the built-in formula and says `heuristic`.

### Your own formula

With `mode: "expression"`, `scoring.expression` is a formula in a small, safe language: arithmetic, comparisons, a
dozen functions, and the signals as variables. Two extra variables bring in the scores above:

- **`formula`** is the built-in formula's score.
- **`model`** is the model's value **shifted up to be 0 or more**: FeedFabric adds the total of the negative goals
  back (1.5 with the defaults), so the example above is `0.26 + 1.5 = 1.76`. The order is unchanged, and multiplying
  it by a decay or a boost can only lower or raise a post as you'd expect. Without the shift, halving a negative
  value would *raise* it. While no model is loaded, `model` is the built-in formula's score instead.

The `fresh` preset is a formula:

```
formula * decay_exp(age_hours, 3) * (1 + 0.3 * log1p(velocity_30m))
```

The built-in score, halved every three more hours, and lifted by up to about ×2 for a post picking up speed. The
[cookbook](/formulas/cookbook/) has more, and [Formula language](/formulas/language/) everything a formula can
contain. A post's `ranking.factors` start with **Profile formula**, the formula's value for it, followed by the
built-in formula's parts for context.

## Adjustments

After the base score, up to three kinds of multiplier apply, in this order, each one added to the post's
`ranking.factors`:

| Adjustment | Multiplier | Set by |
|---|---|---|
| Freshness | `0.5 ^ (age_hours / halfLifeHours)` | `freshness.halfLifeHours` (null: none). Not applied in `newest` mode |
| Boosts | Each matching boost's `multiplier`, one after another | `boosts`: by `tag`, `author`, `hasMedia`, `following`, `notFollowing` or `favourite` |
| Seen already | `variety.seenPenalty` (0.35) | Posts the member saw, opened or engaged with in the last 24 hours. `1` turns it off |

Boosts **compound**: a photo post from a favourite under a profile with `hasMedia ×1.2` and `favourite ×1.5` gets
×1.8.

Because the model's value can be negative, a multiplier isn't applied as a plain multiplication:

| Multiplier | Positive score | Negative score |
|---|---|---|
| Above 1 | `score × m` (up) | `score ÷ m` (towards zero: up) |
| Below 1 | `score × m` (down) | `score ÷ m` (further from zero: down) |
| 0 or less | Sunk to the bottom of the feed (`score − 1,000,000`) | The same |

So a boost above 1 always lifts a post and one below 1 always lowers it, whatever the sign, and a `0` multiplier is
a reliable "put these last" without removing them. To remove posts, use `filters` instead.

`freshness.maxAgeHours` isn't a multiplier: posts older than it are never candidates at all.

## From scores to a feed

Posts are sorted by final score, highest first, and then `DiversityReranker` builds the feed ten at a time, taking
the highest-scoring post that keeps to the profile's `variety` rules (posts per author, topic runs, a share from
outside the member's follows) and filling `exploration.slots` with random picks from below `exploration.fromRank`.
[For You ranking](/how-it-works/for-you/#6-variety-and-exploration) has the details.

That's why a post's `ranking.position` doesn't always follow its `score`: the post at position 3 may have a higher
score than the one at position 2 that had to wait for a different author to come first, and an explored post
(`explored: true`) may score far below the posts around it.

## Seeing the numbers

- **On a feed:** every For You post has `ranking.score` (final), `ranking.factors` (each part and adjustment) and
  `ranking.features` (the raw signals).
- **Before saving:** the Feed studio, or `POST /ranking/preview?viewer=<externalUserId>`, ranks with any settings
  for any member without saving or logging anything. See [A formula editor](/build-a-ui/formula-editor/).
- **After saving:** the dashboard compares profiles and versions on what members actually did.
