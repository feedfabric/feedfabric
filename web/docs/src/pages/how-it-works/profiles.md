---
layout: ../../layouts/Doc.astro
title: Ranking profiles
description: A profile is a named, versioned set of settings for every part of For You. Six presets are built in; staff save their own and compare them on real engagement.
---

## What a profile sets

A profile is JSON (`RankingProfile`). Every part has a default matching the `balanced` preset, so a profile only
needs the parts it changes.

| Part | Settings | Limits |
|---|---|---|
| `name`, `description` | Shown in the admin panel | name 1–60 characters, description up to 300 |
| `sources` | `following`, `topics`, `similarReaders`, `similarMeaning`: each `{ enabled, limit }`; `topTopics`; `lookbackHours` | limits 0–1000 (following) or 0–500; topTopics 1–10; look-back 1–168 h; at least one source on |
| `scoring` | `mode`: `model`, `formula`, `expression` or `newest`; `expression`; `goals` | goals −10 to 10 |
| `scoring.goals` | `open` 0.5, `like` 1, `comment` 3, `longRead` 2, `skip` −1.5 | |
| `freshness` | `halfLifeHours` (or null), `maxAgeHours` (or null) | 0.25–720 h; 1–2160 h |
| `variety` | `authorCap` 2, `topicRun` 2, `outOfNetwork` 2 (per ten), `seenPenalty` 0.35 | 1–10, 1–10, 0–10, 0–1 |
| `exploration` | `slots` 1 (per ten), `fromRank` 30 | 0–3; 10–500 |
| `boosts` | `[{ match, value, multiplier }]`, `match` one of `tag`, `author`, `hasMedia`, `following`, `notFollowing`, `favourite` | up to 20; multiplier 0–10 (0 sinks a post) |
| `filters` | `excludeTags`, `onlyTags`, `mediaOnly`, `minLikes` | up to 50 tags each; minLikes 0–10000 |

Tags and usernames are stored lowercase without `#` or `@`. A profile that breaks a limit is refused with
`400 profile.invalid` and an `errors` list of `{ field, message, position? }`, `field` being the setting's camelCase
path (`variety.authorCap`, `boosts[2].value`).

## Presets

Read-only, always available, and the starting point for your own.

| Key | What it's for |
|---|---|
| `balanced` | The default: people you follow plus the best of everything else, ranked by the trained model. Favourites ×1.5. |
| `community` | Conversation first: comments weighted 5, following ×1.3, favourites ×1.5. |
| `discovery` | New people and ideas: bigger, longer-looking sources, one post per author in ten, five from outside your follows, two exploration slots. |
| `following-first` | Mostly people you follow: following ×2, favourites ×2, similar readers off, one outside post in ten. |
| `fresh` | What's happening now: `formula * decay_exp(age_hours, 3) * (1 + 0.3 * log1p(velocity_30m))`, nothing over 48 hours. |
| `newest` | No ranking: everything from your sources, newest first, no variety rules, no exploration, no seen penalty. |

## Your own formula

With `mode: "expression"`, `expression` is a small, safe language that can only read ranking signals and call a fixed
set of functions, so a profile can't run anything else.

```
model * decay_exp(age_hours, 12)                           the model, halving every 12 hours
log1p(likes + 2 * comments) * decay_exp(age_hours, 6)      popular now
if(topic_match > 0.2, formula, formula * 0.2)              mostly your topics
```

- **Variables:** every [ranking signal](/how-it-works/for-you/#2-signals), plus `age_hours`, `formula` (the built-in
  score) and `model` (the trained model's value with your goals, shifted to zero or more).
- **Functions:** `decay_exp`, `decay_linear`, `decay_gauss`, `min`, `max`, `clamp`, `if`, `ln`, `log1p`, `sqrt`,
  `exp`, `abs`.
- **Operators:** `+ - * / ^`, comparisons (1 if true, 0 if not), `and`, `or`, `not`, brackets.

A formula that doesn't parse is a `profile.invalid` error on `scoring.expression` with the `position` of the problem.
`GET /ranking/vocabulary` returns every variable, function (with signature and description), operator, goal and
preset, the defaults and the default profile's key, ready for an editor.

## Versions

`PUT /ranking/profiles/{key}` creates a profile, or saves a new version of it. Keys are 2–40 lowercase letters,
digits and dashes, starting with a letter (`profile.invalid_key`); preset keys are refused (`profile.read_only`).

- Every For You page is logged with the profile that built it, as `running-club@3`, so the dashboard compares
  profiles **and versions** on real engagement.
- Feeds pick up a new version from their next first page; sessions are per version, so nobody pages through a mix.
- `GET /ranking/profiles/{key}/versions` and `/versions/{n}` show the history; `DELETE /ranking/profiles/{key}`
  removes a saved profile.

## Choosing a profile

- **Per request:** `GET /userfeed/foryou?profile=discovery`. An unknown key is `404 profile.not_found`. Members can
  name any profile; only saving needs a role.
- **By default:** `Ranking:DefaultProfile` (default `balanced`), a preset or a saved profile's key.
- **In the simulator:** `Simulator:ForYouProfiles` splits For You readers between several, round the list.

<div class="tip">

**Letting members choose.** A front end can offer a few profiles as feed tabs ("For you", "Discover", "Latest") by
passing `profile=`. `GET /ranking/profiles` needs the `ranking-admin` role outside Development, so ship your list of
keys with the app rather than reading it at runtime.

</div>

## The Feed studio

The admin panel's Feed studio (`web/admin`, *Feed studio*) edits a profile and previews it live for any member with
`POST /ranking/preview?viewer=<externalUserId>&limit=30`, which ranks without saving, logging or touching that
member's session. Each previewed post shows how far it moved against the saved version, and a summary compares the
mix: share from people you follow, author variety, age and photos.

Outside Development, everything under `/ranking` needs the `ranking-admin` role
(`Auth:Roles:RankingAdmin`), because saving a profile changes everyone's feed.
