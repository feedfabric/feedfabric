---
layout: ../layouts/Doc.astro
title: Contributing to the docs
description: How this site is built, how to run it, add or change a page, and how changes reach the live site. Every page has an "Edit this page" link; this is what happens after you press it.
---

These docs live in the FeedFabric repository, in `web/docs`, next to the code they describe, so a change to how
something works and the page that explains it can land in the same pull request. Corrections of any size are
welcome: a typo, an example that no longer works, a page that left you with a question.

## Running the site

You need Node 22 or later.

```bash
make docs                 # installs, then serves the site at http://localhost:5186 with live reload
# or
cd web/docs && npm install && npm run dev
```

Search only exists in a built site, because Pagefind indexes the finished pages:

```bash
cd web/docs
npm run build             # Astro, then Pagefind: everything in dist/
npm run preview           # serves dist/ at http://localhost:5186
```

`npm run preview:cloudflare` serves `dist/` the way Cloudflare does (trailing slashes, the 404 page, `_headers`),
which is worth a look after changing `wrangler.jsonc` or `public/_headers`.

## How it's put together

| Path | What it is |
|---|---|
| `src/pages/**/*.md` | The pages. A file's path is its address: `src/pages/formulas/language.md` is `/formulas/language/` |
| `src/pages/index.astro` | The overview, as an Astro page because of its diagram |
| `src/nav.ts` | The sidebar, in reading order; previous and next links follow it |
| `src/layouts/Doc.astro` | The frame of every page: header, search, sidebar, contents, edit link, footer |
| `src/styles/docs.css` | The site's styles, on top of FabricUI's tokens (`web/fabric-ui/src/styles/fabric.css`) |
| `src/site.ts` | The repository's address, for the GitHub and edit links |
| `public/` | Copied as is: the icon, `_headers` for Cloudflare, `robots.txt` |
| `wrangler.jsonc` | How Cloudflare serves it ([Cloudflare](/deploy/cloudflare/#deploying-these-docs)) |

It's [Astro](https://astro.build) with no UI framework and almost no JavaScript: the theme switch, the phone menu and
search.

## Adding a page

1. Create the Markdown file under `src/pages/`, with the layout, a title and a one-sentence description (shown under
   the title, in search results and in link previews):

   ```markdown
   ---
   layout: ../../layouts/Doc.astro
   title: Webhooks
   description: Getting told when something happens, rather than asking.
   ---

   ## The first section
   ```

   The `layout` path is relative to the file, so it has one `../` per folder below `src/pages`.
2. Add it to `src/nav.ts` where it belongs in the reading order.
3. Link to it from the pages that should lead there.

Every `##` and `###` heading gets an anchor and goes in the *On this page* list, so link to sections as
`/formulas/language/#functions`.

## Writing style

The existing pages follow a few habits; new ones should too.

- **British English**, plain words, short sentences. Address the reader as "you", and members as "members".
- **Describe what the code does today**, not what it should do. Where something is missing, say so plainly and point
  to `docs/production-readiness.md`.
- **Exact names in code style**: settings (`Ranking:DefaultProfile`), routes (`GET /userfeed/foryou`), signals
  (`topic_match`), error codes (`profile.invalid`).
- **Tables for reference, prose for explanation.** A setting, a signal or an error code is a table row; why it
  exists is a paragraph.
- **Examples that run.** Code should work if pasted, against the API as it is. If you change an endpoint, search
  the docs for it.
- **Callouts** are HTML blocks with a blank line inside, so the Markdown in them still works:

  ```html
  <div class="note">

  **Worth knowing.** Something that isn't obvious from the rest of the page.

  </div>
  ```

  `note` for background, `tip` for a better way, `warn` for something that will bite, `why` for the reason behind a
  design.

## Checking a change

```bash
cd web/docs && npm run build
```

The build fails on broken Markdown, a missing layout or a TypeScript error in the layout. It doesn't check links,
so click through the pages you changed. CI builds the site on every pull request that touches `web/docs`.

## From pull request to live site

1. Open a pull request. The **Docs** workflow builds the site and, for branches in the repository, uploads a preview
   version on Cloudflare; its address is in the run's summary.
2. Once it's merged to `main`, the same workflow deploys it. The live site updates within a minute or two.

The workflow is `.github/workflows/docs.yml`; [Cloudflare](/deploy/cloudflare/#from-github-actions) has its setup.

## Beyond the docs

The repository's `CODE_OF_CONDUCT.md` applies here as everywhere in the project. For a bug in FeedFabric itself rather than in a page, [open an issue](https://github.com/feedfabric/feedfabric/issues/new), and
for anything security-related, follow `SECURITY.md` instead of opening a public issue.
