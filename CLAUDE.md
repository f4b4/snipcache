# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**SnipCache** (a "knowledge store") is being implemented phase by phase.
**Phase 0 (repository bootstrap)** is done. **Phase 1 (site builder)** is
implemented: `site/` (Astro + Starlight + Pagefind) and the shared slug rule
`frontend/shared/content-rules` (`@snipcache/content-rules`). **Phase 2
(reusable workflow, template, first deployment)** is implemented:
`.github/workflows/build-site.yml`, tag `v1`, both sibling repos deploying to
Pages, the owner's real snippets migrated. **Phase 3 (editor backend)** is
implemented: `backend/shared/SnipCacheAuth` (copy of CestiusAuth),
`backend/apps/SnipCacheEditor` (content API over Octokit or an in-memory fake)
and their tests; the manual read/write check against the real content repo
passed. **Phase 4 (editor frontend)** is implemented:
`frontend/shared/snipcache-auth` (`@snipcache/auth`, copy of `@cestius/auth`)
and `frontend/apps/editor` (Vite + React + Monaco SPA); the full edit flow was
checked in Chrome against the fake repository and read-only against the real
one. Still open from phase 4's "done when": a save against the real repo
through the SPA and sign-in through the real Keycloak (the client is created
in phase 6). Next: phase 5 (bot API).

Repositories (all public, owner `f4b4`):

- `https://github.com/f4b4/snipcache`: this code monorepo (`origin`)
- `https://github.com/f4b4/snipcache-template`: template repo (marked as
  template), site at `https://f4b4.github.io/snipcache-template/`
- `https://github.com/f4b4/snipcache-content-f4b4`: the owner's content repo,
  site at `https://f4b4.github.io/snipcache-content-f4b4/`

Both are checked out next to this repo (`../snipcache-template`,
`../snipcache-content-f4b4`); their "Deploy SnipCache" workflow calls
`build-site.yml@v1`.

Documents:

- [doc/snipcache-architecture-and-plan.md](doc/snipcache-architecture-and-plan.md) —
  **source of truth**: decisions, architecture, component design, phased plan.
  Read it before implementing anything.
- [doc/content-repo-guide.md](doc/content-repo-guide.md) — user guide for
  content repos: creating an instance, conventions, workflow inputs.
- [doc/SnipCache-features.md](doc/SnipCache-features.md) — the original
  concept and feature list (historical; the plan supersedes its open questions).
- `SnipCache.code-workspace` — VS Code workspace.

When implementing a phase, follow the plan's layout and update this file with the
build, test and run commands as they come into existence.

## Commands

Requirements: Node 22.12 or newer (any even-numbered LTS; apt's `nodejs` is
fine), pnpm 12 (globally installed with the standalone installer; not pinned
in `package.json`, CI sets the major in `pnpm/action-setup`), .NET 10 SDK.

```sh
pnpm install
pnpm build                              # build site/ and frontend/** packages
pnpm test                               # Vitest, one runner for all packages (vitest.config.ts)
pnpm test:watch
dotnet build SnipCache.slnx
dotnet test --solution SnipCache.slnx   # Microsoft.Testing.Platform (global.json)
```

Site builder (`site/`, see plan section 4.1):

```sh
pnpm --filter site dev                  # prepare-content on site/sample-content + astro dev (no search)
pnpm --filter site build                # prepare-content + astro build (+ Pagefind index)
pnpm --filter site preview              # serve dist/; needed to try search; Astro 7 daemonizes it
                                        #   (stop with: pnpm --filter site exec astro preview stop)
pnpm --filter site check-build          # smoke test of dist/ (pagefind, pages, raw copies, base, edit links)
pnpm --filter site test
SNIPCACHE_CONTENT_DIR=../../snipcache-content-f4b4 pnpm --filter site build   # real content
```

Build environment: `SNIPCACHE_CONTENT_DIR` (default `site/sample-content`,
relative paths resolve from `site/`), `SNIPCACHE_SITE`, `SNIPCACHE_BASE`
(e.g. `/snipcache-sample`), `SNIPCACHE_TITLE`, `SNIPCACHE_EDITOR_URL`.
`check-build` must see the same values as the build. Generated files
(`site/src/content/docs/`, `site/public/raw/`, `site/src/generated/`) are
gitignored and rewritten on every run. Astro and Starlight are pinned to a
minor (`~`); upgrade deliberately and re-run the build and `check-build`.

Reusable workflow (`.github/workflows/build-site.yml`, plan section 4.2):
content repos call it at `@v1` and check out the builder at the
`builder_ref` input (default `v1`), so `v1` is what every content repo builds
with. It derives `SNIPCACHE_SITE`/`SNIPCACHE_BASE` from the repo name, runs
`build` and `check-build`, and deploys to Pages. After a compatible change
on `main` (and green CI), move the tag; breaking changes get `v2`:

```sh
git tag -f -a v1 -m "SnipCache site builder v1" && git push -f origin v1
```

Editor backend (`backend/apps/SnipCacheEditor`, plan section 4.3):

```sh
dotnet run --project backend/apps/SnipCacheEditor --launch-profile https   # https://localhost:7240
dotnet test --project backend/tests/SnipCacheEditor.Tests
```

- Development uses the in-memory fake repository seeded from
  `site/sample-content` (`GitHub:UseFake`, `GitHub:FakeSeedDirectory` in
  `appsettings.Development.json`); restarting resets it.
- While `Keycloak:Authority` is empty (the default), Development maps
  `GET /auth/dev-login?name=&email=&roles=`, which issues a session cookie
  without Keycloak. The cookie is `Secure`, so use the https URL:
  `curl -sk -c jar "https://localhost:7240/auth/dev-login?name=me"`, then
  `curl -sk -b jar https://localhost:7240/api/tree`. Mutating `/api` calls need
  `-H 'X-CSRF: 1'`.
- Against the real repo: `dotnet user-secrets set GitHub:Token <PAT> --project
  backend/apps/SnipCacheEditor` and run with `GitHub__UseFake=false`.
  Every save is a real commit and starts "Deploy SnipCache".
- `dotnet build` regenerates `backend/apps/SnipCacheEditor/openapi/openapi.json`
  (committed; Orval reads it in phase 4). Commit it with every API change.
  The generator starts the host without secrets, so GitHub settings are not
  validated under `GetDocument.Insider`.
- Health check: `GET /health` (anonymous, not proxied by nginx).

Editor frontend (`frontend/apps/editor`, plan section 4.4):

```sh
dotnet run --project backend/apps/SnipCacheEditor --launch-profile https   # backend first
pnpm dev                                # = pnpm --filter editor dev; http://localhost:5173
pnpm --filter editor build              # tsc + vite build
pnpm --filter editor generate:api       # Orval: openapi.json → src/api/generated (committed)
```

- Vite proxies `/api`, `/auth` and `/bot` to `https://localhost:7240`. With
  `localhost.pem`/`localhost-key.pem` from mkcert in `frontend/apps/editor`
  it serves https (needed for the real Keycloak redirect URI
  `https://localhost:5173/auth/*`); without them http is enough for dev.
- Without `Keycloak:Authority`, Development maps stand-ins for `/auth/login`
  (signs in as `dev` and returns to `returnUrl`), `/auth/me` and
  `/auth/logout` (`DevAuthEndpoints.cs`), so the SPA's normal sign-in flow
  works against the fake repository.
- Regenerate the client after every API change (`dotnet build`, then
  `generate:api`) and commit both; CI fails when either is out of date.
- Monaco is bundled locally (`src/features/editor/monaco.ts`); a language in
  `languages.ts` must also be registered there. It is uncontrolled
  (`defaultValue`) on purpose, see `CodeEditor.tsx`.
- Component tests mock the generated hooks and replace `CodeEditor` with a
  textarea; Vitest runs `frontend/**` under jsdom and `site/**` plus
  `content-rules` under Node (projects in `vitest.config.ts`).

pnpm enforces a minimum release age for new dependency versions. When an
install fails because a version is too new, require an older one instead of
adding `minimumReleaseAgeExclude` entries (pnpm may write them into
`pnpm-workspace.yaml` by itself; revert that).

New .NET projects must be added to `SnipCache.slnx`; new frontend packages are
picked up by `pnpm-workspace.yaml`. CI (`.github/workflows/ci.yml`) runs the
same commands on push to `main` and on PRs. CI installs with `--frozen-lockfile`, so commit
`pnpm-lock.yaml` with every dependency change. Dependency build scripts are
denied unless listed under `allowBuilds` in `pnpm-workspace.yaml` (esbuild's
is not needed). CI builds the sample site with a non-root base and an editor
URL, then runs `check-build`.

## Purpose

Collect, edit and search know-how snippets: Markdown recipes and how-tos, SQL,
PowerShell/batch/shell scripts, Markdown tables (e.g. keyboard shortcuts), and
knowledge captured from AI chats.

## Architecture in one paragraph

Three GitHub repos. This **code monorepo** (`snipcache`) holds an Astro + Starlight
site builder (`site/`), a reusable GitHub Actions workflow that content repos call,
and the editor (`backend/` ASP.NET Core 10 minimal API, `frontend/` React + Monaco).
A **template repo** (`snipcache-template`) is what users clone; it contains only
folders of snippet files plus one workflow named **"Deploy SnipCache"** that builds
the site and a Pagefind search index on every push and publishes to GitHub Pages.
The **editor** at `https://snipcache.freeddns.org` authenticates with Keycloak
(BFF pattern: OIDC code flow + PKCE on the server, HttpOnly cookie, CSRF header),
reads and writes the content repo through the GitHub REST API via Octokit.net (one
commit per save), and offers a bot endpoint `POST /bot/snippets` with an API key.

## Key decisions (do not re-open without asking)

- Search: build-time Pagefind index, no server search in v1.
- Static site: Astro + Starlight; non-Markdown files become a page with one code
  block, a copy-whole-file button and a raw link.
- Titles: Markdown pages use frontmatter `title`, else first H1 (removed from the
  body), else file name without extension; other files use the full file name.
- Git writes: GitHub Contents API with the file SHA for conflict detection; folders
  are created by adding a `README.md`, which becomes the folder's index page.
- Auth code is **copied** from the Cestius repo
  (`/run/media/chris/Projects/github/cestius/main`): `backend/shared/CestiusAuth`
  → `SnipCacheAuth`, `frontend/shared/cestius-auth` → `@snipcache/auth`. Keep the
  copies otherwise identical. Cestius is also the reference for conventions (Orval
  hooks instead of raw fetch, minimal APIs, xunit v3 on Microsoft.Testing.Platform,
  Vitest + RTL, Tailwind v4, systemd + nginx deploy scripts).
- Runtime: .NET 10, Node >= 22.12 (build-time only, not pinned except in
  `build-site.yml`), pnpm, React 19, TypeScript.
- v1 editor scope: open, edit, save, new file, new folder. Rename/move/delete,
  preview, tags, images, MCP server and GitHub App auth are backlog.

## Conventions

- Documentation in English; the project name and some terms may be German.
- Naming: kebab-case for frontend packages and modules, PascalCase for .NET
  projects with one flat namespace per project (same as Cestius).
- Secrets (GitHub token, bot API key) never go into `appsettings*.json`; they are
  loaded from an environment file in production and user-secrets in development.
