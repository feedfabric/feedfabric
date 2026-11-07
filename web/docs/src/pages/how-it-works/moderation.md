---
layout: ../../layouts/Doc.astro
title: Moderation and reports
description: Held and flagged content waits in a review queue; members report what slipped through; moderators approve, remove, suspend and verify from the admin panel.
---

## Content states

Posts and comments are `Visible`, `Held` or `Removed` (and separately can be soft-deleted by their author).

| State | Who sees it | How it gets there |
|---|---|---|
| Visible | Everyone | Passed the checks, flagged, or approved |
| Held | Only its author (`isHeld: true`) | The toxicity model held it, or enough members reported it |
| Removed | Nobody, its author included | A moderator removed it |

Every check and decision is a `ModerationEvent` with its action (`blocked`, `held`, `flagged`, `reported`,
`reviewed`), category, score and an excerpt. Those events are the moderation log, and each decision is stored on
the events that put the item in the queue, which makes them labelled examples for retraining the classifier.

## The review queue

`GET /admin/moderation/queue` lists what's waiting, **oldest first** so nothing waits forever: held and flagged posts
and comments that no moderator has decided on yet and their author hasn't deleted. Each item comes with the events
that put it there and the reports since it was last reviewed, by reason.

```http
POST /admin/moderation/posts/{postId}/decision        { "decision": "approved" }
POST /admin/moderation/comments/{commentId}/decision  { "decision": "removed" }
```

- **Approve** shows it to everyone. A comment held when it was written didn't count as engagement; approving it now
  counts it.
- **Remove** hides it from everyone, its author included. Moderators can remove something nobody flagged; that's
  recorded as a manual review.

`GET /admin/moderation/overview` has what's waiting and the last day's blocks, holds, reports and decisions;
`GET /admin/moderation/log?action=blocked|held|reported|reviewed&limit=100` the latest events;
`GET /admin/moderation/reports?limit=100` the latest reports with what they're about as it is now.

## Reports from members

```http
POST /userfeed/posts/{postId}/report        { "reason": "harassment" }
POST /userfeed/comments/{commentId}/report  { "reason": "spam" }
```

`reason` is one of `spam`, `harassment`, `hate`, `threat`, `sexual`, `self_harm`, `other` (anything else is
`400 validation_failed`). Both return `204`.

- Reporting the same thing twice counts once. Reporting your own post or comment does nothing (still `204`).
- Reposts can't be reported; report the original.
- When **`Moderation:ReportsToHold` (3) different members** have reported a visible post or comment since a moderator
  last looked at it, it's held and joins the review queue.

<div class="why">

**For your UI:** put *Report* in a post's and comment's overflow menu with the seven reasons in your own words. The
response gives nothing away (no count, no state), so just confirm it was received; consider hiding the item for the
reporter locally.

</div>

## Users

| Route | What |
|---|---|
| `GET /admin/users?q=&status=&sort=&offset=&limit=` | Search; `status` `active` or `suspended`, `sort` `username`, `posts`, `reports` or `flags` |
| `GET /admin/users/{externalUserId}` | A member's profile for staff |
| `PUT /admin/users/{externalUserId}/status` `{ "status": "suspended" }` | Suspend or reinstate; recorded in the moderation log |
| `PUT /admin/users/{externalUserId}/verified` `{ "verified": true }` | Mark as verified, or not |

A suspended member can read but not write (`403 user.suspended`); their posts and comments disappear for everyone
else while they're suspended. See [Posting and safety](/how-it-works/posting/#suspended-members).

`GET /admin/posts` and `GET /admin/posts/{postId}` show posts in every state, with comments, reports and history.

## Who can do it

Everything under `/admin/moderation`, `/admin/users`, `/admin/posts` and `/admin/media` needs the **moderator** role
(`Auth:Roles:Moderator`, default `moderator`); `/admin/dashboard` and `/admin/system` need moderator or ranking admin.
In Development any signed-in user passes. See [Sign-in and users](/auth/).

The admin panel (`web/admin`, `make admin`) is the UI for all of this: a live dashboard, the review queue, reports,
the moderation log, users and posts, the Feed studio, and the server's settings and models.
