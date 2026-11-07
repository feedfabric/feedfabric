---
layout: ../../layouts/Doc.astro
title: Formula language
description: The complete reference for a ranking profile's own formula. Numbers, operators and their precedence, every variable and function, how edge cases behave, and the errors you can get.
---

A ranking profile with `scoring.mode` `expression` scores every candidate with its own formula,
`scoring.expression`. The language is deliberately tiny: it can read the post's [signals](#variables) and call
[a fixed set of functions](#functions), and nothing else. There are no loops, no assignments, no strings and no way to
reach anything outside the post being scored, so a saved profile can never do more than produce a number.

```json
{
  "name": "Popular now",
  "scoring": { "mode": "expression", "expression": "log1p(likes + 2 * comments) * decay_exp(age_hours, 6)" }
}
```

The parser and evaluator are `ScoreExpression` in
`src/FeedFabric.Application/Services/Ranking/Profiles/ScoreExpression.cs`; `GET /ranking/vocabulary` returns the
variables, functions and examples below as JSON.

## The shape of a formula

A formula is one expression: numbers and variables combined with operators and function calls.

```
model * decay_exp(age_hours, 12)
if(follows_author, formula * 1.5, formula)
(likes + 2 * comments + 1) / (impressions + 20)
topic_match > 0.3 and has_media
```

- **Numbers** are digits with an optional decimal point: `3`, `0.25`, `.5`. There's no exponent form (`1e3`): write
  `1000`. A negative number is the minus operator in front of one: `-1.5`.
- **Names** (variables, functions, `and`, `or`, `not`) are case-insensitive: `Age_Hours` is `age_hours`.
- **Spaces and line breaks** are ignored between tokens.
- **Length:** up to 500 characters, and up to 40 levels of nesting (brackets and unary operators). Leading and
  trailing spaces are trimmed when the profile is saved.

## Operators

Loosest first; operators on the same row are evaluated left to right, except `^`.

| Precedence | Operators | Meaning |
|---|---|---|
| 1 (loosest) | `or` | 1 if either side isn't 0, otherwise 0 |
| 2 | `and` | 1 if both sides aren't 0, otherwise 0 |
| 3 | `>` `<` `>=` `<=` `==` `!=` | 1 if true, 0 if not. **One per level:** `0 < x < 1` is an error; write `x > 0 and x < 1` |
| 4 | `+` `-` | Add, subtract |
| 5 | `*` `/` | Multiply, divide. **Dividing by 0 gives 0** |
| 6 | `-` `not` (in front) | Negate; `not x` is 1 when `x` is 0, otherwise 0 |
| 7 (tightest) | `^` | Power, **right to left**: `2 ^ 3 ^ 2` is `2 ^ 9` = 512 |
| | `( )` | Grouping |

Two consequences worth knowing:

- `-2 ^ 2` is `-(2 ^ 2)` = **−4**, as in maths. Write `(-2) ^ 2` for 4.
- `^` can take a signed exponent directly: `age_hours ^ -0.5` works.

`and` and `or` stop early: `x > 0 and y / x > 2` never looks at the right-hand side when `x` is 0. Any number can
be a condition; 0 is false and everything else is true, so `follows_author and has_media` works as is.

## Variables

Every variable is a number for the post being scored, seen from the member whose feed it is.

### About the post

| Variable | Range | Meaning |
|---|---|---|
| `age_minutes` | 0 and up | Minutes since it was posted |
| `age_hours` | 0 and up | Hours since it was posted (`age_minutes / 60`) |
| `likes` | 0 and up | Number of likes |
| `comments` | 0 and up | Number of comments |
| `velocity_30m` | 0 and up | Likes and comments in the last 30 minutes |
| `impressions` | 0 and up | Times it's been seen, from your apps' impressions |
| `opens` | 0 and up | Times it's been opened |
| `ctr` | 0 to 1 | Share of views that opened it: `(opens + 1) / (impressions + 20)` |
| `engagement_rate` | 0 to 1 | Share of views that liked or commented: `(likes + comments + 1) / (impressions + 20)`, at most 1 |
| `avg_dwell_s` | 0 to 60 | Average seconds on screen |
| `has_media` | 0 or 1 | 1 if it has photos |
| `content_length` | 0 and up | Characters of text |
| `author_followers_log` | 0 and up | `ln(1 + the author's followers)`: 0 for none, about 4.6 for 100, 6.9 for 1,000 |

### About this member and the post

| Variable | Range | Meaning |
|---|---|---|
| `follows_author` | 0 or 1 | 1 if the member follows the author |
| `author_affinity` | 0 and up | How much the member engages with the author: likes count 1, comments 2; *Not interested* takes 3 off and *Show more* adds 3 |
| `topic_match` | 0 to 1 | How much of what the member engages with is on this post's topics |
| `session_topic_match` | 0 to 1 | The same, for what they engaged with in the last 15 minutes |
| `semantic_match` | 0 to 1 | How close in meaning the post is to the member's recent likes (MiniLM embeddings) |
| `viewer_author_ctr` | 0 to 1 | How often the member opens this author's posts after seeing them: `(opened + 1) / (seen + 10)` |
| `viewer_author_seen` | 0 and up | How many of the author's posts the member saw in the last week |

### Scores

| Variable | Meaning |
|---|---|
| `formula` | The [built-in formula](/formulas/#the-built-in-formula)'s score: 1 or more for most fresh posts, falling with age |
| `model` | The [trained model's value](/formulas/#the-models-value) with the profile's goals, shifted to be 0 or more. While no model is loaded, the built-in formula's score |

`position` is a signal but **not** a variable (it's always 0 when scoring), so using it is an error. A formula that
uses `model` makes the response's `ranker` `expression+model:<version>`; one that doesn't is just `expression`, and
FeedFabric doesn't run the model at all for it.

## Functions

| Function | Returns | Edge cases |
|---|---|---|
| `decay_exp(t, half_life)` | `0.5 ^ (t / half_life)`: 1 at t = 0, halving every `half_life` | `half_life` ≤ 0 gives 1 (no decay); negative `t` counts as 0 |
| `decay_linear(t, zero_at)` | `1 − t / zero_at`, never below 0: a straight line from 1 to 0 at `zero_at` | `zero_at` ≤ 0 gives 1; negative `t` counts as 0 |
| `decay_gauss(t, scale)` | `0.5 ^ ((t / scale) ^ 2)`: flat at first, 0.5 at `t = scale`, then falling fast | `scale` ≤ 0 gives 1; negative `t` counts as 0 |
| `min(a, b)` | The smaller | |
| `max(a, b)` | The larger | |
| `clamp(x, low, high)` | `x`, kept between `low` and `high` | If `low` is above `high` they're swapped |
| `if(condition, then, else)` | `then` when `condition` isn't 0, otherwise `else` | Only the branch it returns is evaluated |
| `ln(x)` | Natural logarithm | 0 for `x` ≤ 0 |
| `log1p(x)` | `ln(1 + x)`: grows fast at first, then slowly; the usual choice for counts | 0 for `x` ≤ −1 |
| `sqrt(x)` | Square root | 0 for `x` < 0 |
| `exp(x)` | e to the power of `x` | `x` above 50 counts as 50 |
| `abs(x)` | `x` without its sign | |

Each function takes exactly the number of values shown. The three decays compared, with a scale of 6 hours:

| `age_hours` | 0 | 3 | 6 | 12 | 24 |
|---|---|---|---|---|---|
| `decay_exp(age_hours, 6)` | 1 | 0.71 | 0.5 | 0.25 | 0.06 |
| `decay_linear(age_hours, 6)` | 1 | 0.5 | 0 | 0 | 0 |
| `decay_gauss(age_hours, 6)` | 1 | 0.84 | 0.5 | 0.06 | 0.00002 |

`decay_exp` is the gentle, never-quite-zero choice; `decay_linear` is a hard cut-off; `decay_gauss` keeps posts
almost level for a while and then drops them quickly.

## How evaluation behaves

- **It never fails at ranking time.** A formula that saved is valid; every operation is defined for every input.
- **Not-a-number and infinity become 0.** If the final result isn't a finite number (`(-8) ^ 0.5`, say, or a huge
  power), the post scores 0. Only the final result is checked, so a bad value part-way through spoils the whole
  formula for that post: guard inputs with `max`, `clamp` or `if` rather than relying on it.
- **Scores can be negative.** That's allowed and sorts below zero. [Adjustments](/formulas/#adjustments) (freshness,
  boosts, seen already) still lift and lower negative scores the right way.
- **Only order matters.** The feed is the scores sorted highest first, so `formula * 10` ranks exactly like
  `formula`. Scale only matters inside your formula, when you add things together.
- **Adjustments come after.** Your formula's value is the base score; the profile's freshness, boosts and seen
  penalty then multiply it. If your formula already decays with age, leave `freshness.halfLifeHours` null, or you'll
  decay twice.

## Errors

A formula is checked when a profile is saved (`PUT /ranking/profiles/{key}`) or previewed (`POST /ranking/preview`).
One that doesn't parse is refused with `400 profile.invalid`, with an entry for `scoring.expression` giving the
problem and the **position** (0-based, in characters) where it was found:

```json
{
  "status": 400,
  "code": "profile.invalid",
  "detail": "Some settings need changing.",
  "errors": [
    { "field": "scoring.expression", "message": "There's no variable called 'age'.", "position": 12 }
  ]
}
```

| Message | Cause |
|---|---|
| The formula is empty. | `mode` is `expression` with no `expression`, or only spaces |
| The formula is longer than 500 characters. | Too long |
| There's no variable called 'x'. | A misspelt variable, or `position` |
| There's no function called 'x'. | A name followed by `(` that isn't a function: `log(x)` (use `ln`), `pow(a, b)` (use `^`) |
| f(…) takes n values, not m. | Wrong number of arguments: `decay_exp(age_hours)` |
| 'x' isn't a number. | A malformed number such as `1.2.3` |
| Unexpected 'x'. | Something that can't go there: `1e3`, `a < b < c`, a stray `,` or `)`, `%`, `=` for `==` |
| Expected ')'. | A bracket or call that isn't closed |
| The formula ends too soon. | It ends after an operator: `formula *` |
| The formula is nested too deeply. | More than 40 levels of brackets or unary operators |

Show the `message` next to the formula and put the cursor at `position`;
[A formula editor](/build-a-ui/formula-editor/) does exactly that. The messages are English and may be reworded, so
match on `field` and `code`, never on `message`.

## Grammar

For anyone writing a highlighter or a client-side checker, the full grammar:

```
formula     = or
or          = and { "or" and }
and         = comparison { "and" comparison }
comparison  = sum [ ( ">=" | "<=" | "==" | "!=" | ">" | "<" ) sum ]
sum         = product { ( "+" | "-" ) product }
product     = unary { ( "*" | "/" ) unary }
unary       = "-" unary | "not" unary | power
power       = atom [ "^" unary ]
atom        = number | variable | function "(" [ or { "," or } ] ")" | "(" or ")"
number      = digit { digit | "." } | "." { digit | "." }
name        = ( letter | "_" ) { letter | digit | "_" }
```

Words (`and`, `or`, `not`) only count as words when they aren't part of a longer name.
