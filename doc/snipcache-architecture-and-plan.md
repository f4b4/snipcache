# SnipCache: Architecture and Implementation Plan

Status: design approved on 2026-10-24. This document turns the concept in
[SnipCache-features.md](SnipCache-features.md) into a concrete
architecture and a phased implementation plan. The project was called
"SnipCache" during the concept phase and is now **SnipCache**.

Audience: the developer and the coding agents implementing the project. Each
phase in section 6 is meant to be executed as one or more focused work sessions.

---

## 1. Decisions

| # | Topic | Decision |
|---|-------|----------|
| 1 | Search | Build-time index with **Pagefind** (bundled with Starlight). No server-side search, no bot search API in v1. |
| 2 | Static site | **Astro + Starlight** docs theme, deployed to GitHub Pages. |
| 3 | Repositories | Three repos: code monorepo `snipcache`, template repo `snipcache-template`, and the owner's own content repo created from the template. |
| 4 | Git write path | **GitHub REST API via Octokit.net**, no local clone. One commit per save. |
| 5 | Authentication | **BFF pattern** copied from Cestius: ASP.NET Core runs OIDC code flow + PKCE against Keycloak, session in an encrypted HttpOnly cookie, CSRF header on mutating calls. |
| 6 | Bot API | v1 is a single REST endpoint to **save** a snippet, authenticated with an API key. MCP server and search come later. |
| 7 | Hosting | Keycloak: `https://auth-colmena.freeddns.org`, dedicated client. Editor at `https://snipcache.freeddns.org` on the Ubuntu 24.04 server, deployed like the Cestius apps (systemd-managed Kestrel behind nginx). Reading site on GitHub Pages. |
| 8 | Non-Markdown files | Rendered as a page with the whole file in one highlighted code block, a copy-whole-file button and a raw download link. |
| 9 | Naming | Markdown pages are named after the first H1, else the file name without extension. Other files are named with their full file name. No images, no tags in v1. |
| 10 | Editor scope v1 | Open, edit, save, new file, new folder. Rename, move, delete and preview are phase 2. |
| 11 | Runtime | .NET 10 (LTS), Node 22.12 or newer (any even-numbered LTS; Node is build-time only), pnpm, React 19, TypeScript. |

Two choices made while writing this plan, both easy to change:

- **GitHub credential for v1 is a fine-grained personal access token** scoped to
  the content repo with Contents read/write. A GitHub App gives rotating tokens
  but needs JWT signing and token caching. It is listed in the backlog.
- **The bot endpoint lives under `/bot/`, not `/api/`**, so the CSRF guard that
  protects the cookie-authenticated `/api/*` routes does not apply to it.

---

## 2. Architecture overview

```
                       GitHub                                      Ubuntu server
 ┌──────────────────────────────────────────────┐        ┌──────────────────────────────────┐
 │  snipcache (code repo, public)               │        │  nginx  (TLS, snipcache.freeddns) │
 │   site/        Astro + Starlight builder     │        │    /            → SPA (static)    │
 │   .github/workflows/build-site.yml (reusable)│        │    /api /auth /bot → Kestrel      │
 │   backend/     SnipCacheEditor (ASP.NET)     │        │                                   │
 │   frontend/    editor SPA (React + Monaco)   │        │  SnipCacheEditor (systemd)        │
 └──────────────────────────────────────────────┘        │    BFF auth ── OIDC ──► Keycloak  │
                 ▲ uses workflow @v1                     │    Octokit.net ── REST ──► GitHub │
 ┌──────────────────────────────────────────────┐        └──────────────────────────────────┘
 │  snipcache-content-f4b4 (from snipcache-template)   │ ◄── commits via Contents API ─────┘
 │   folders + .md/.ps1/.sh/.bat/.sql files     │
 │   .github/workflows/deploy.yml               │
 │      name: "Deploy SnipCache"                │
 │      on push → build-site.yml@v1             │
 │        → GitHub Pages (HTML + Pagefind index)│
 └──────────────────────────────────────────────┘
                 │
                 ▼
        Reader's browser: static pages, Pagefind search runs client-side,
        copy buttons, "Edit" link → https://snipcache.freeddns.org/edit/<path>
```

### 2.1 The three repositories

**`snipcache`** (this repository, public). A pnpm + .NET monorepo holding all
code: the site builder, the reusable deploy workflow, the editor backend and
frontend, deploy scripts and documentation. Content repos reference its
workflow by tag, so improvements reach every instance without copying files.

**`snipcache-template`** (public, marked "Template repository"). The clean
example a new user starts from. Contains a short README, two or three example
folders with example snippets, and one workflow file named **Deploy SnipCache**
that calls the reusable workflow. No build code.

**The owner's content repo** (public, created from the template). Holds the
real snippets. The editor commits here. GitHub Pages serves the generated site
at `https://f4b4.github.io/<repo>/`.

### 2.2 Reading site

A static Starlight site. Starlight provides the folder tree sidebar, Markdown
rendering with Expressive Code (copy button on every code block), Pagefind
search built at `astro build` time, light and dark themes, and per-page edit
links. SnipCache adds:

- a **prepare-content** step that turns the content repo's file tree into
  Starlight pages (section 4.1),
- a **PageTitle override** with three actions: copy whole file, open raw file,
  edit in SnipCacheEditor,
- a generated sidebar that mirrors the folder tree.

No server component. Search is a set of static index chunks that the browser
loads on demand.

### 2.3 Edit server

One ASP.NET Core 10 minimal API project, `SnipCacheEditor`, plus the React SPA
`editor`. Responsibilities:

- Keycloak login via the BFF library (copied from Cestius as `SnipCacheAuth`
  and `@snipcache/auth`).
- Read the content repo tree and files through the GitHub REST API.
- Create and update files with one commit per save, with the Keycloak user as
  commit author.
- Create folders by creating a `README.md` inside them (git has no empty
  folders; the README becomes the folder's index page on the site).
- Bot endpoint to save a snippet with an API key.

Every push the editor makes triggers "Deploy SnipCache" in the content repo,
which rebuilds the site and the search index. Commits made with a personal
access token or GitHub App token do trigger workflows; commits made with the
built-in `GITHUB_TOKEN` would not, which is why the editor needs its own token.

### 2.4 Flows

**Publish.** Push to content repo → "Deploy SnipCache" → reusable workflow checks
out `snipcache` at the pinned tag and the content repo → `prepare-content` →
`astro build` (Pagefind runs as part of it) → upload Pages artifact → deploy.
Expected duration: one to two minutes.

**Edit from the site.** Reader clicks "Edit" on a page →
`https://snipcache.freeddns.org/edit/<original path>` → SPA's `ProtectedRoute`
sees no session → `/auth/login?returnUrl=/edit/<path>` → Keycloak → back with
cookie → SPA loads tree and file → user edits in Monaco → Ctrl+S → `PUT
/api/files/<path>` with the file's SHA → commit → tree cache invalidated →
"Deploy SnipCache" runs → site updated.

**Bot save.** Agent calls `POST /bot/snippets` with `X-Api-Key` and a JSON body
(folder, file name, content, message) → same commit service → same deploy.

### 2.5 Security model

- Browser sessions: encrypted HttpOnly, Secure, SameSite=Lax cookie. Tokens
  never reach the browser. Mutating `/api` calls need the `X-CSRF: 1` header
  (the guard middleware from Cestius).
- Editor access can be limited to users holding the Keycloak role
  `snipcache-editor` (realm or client role mapped to the `roles` claim). This
  matters because the Keycloak realm may serve other apps and users.
- GitHub token and bot API key are secrets, loaded from an environment file
  that systemd passes to the process. They never ship in `appsettings*.json`.
- The bot API key is compared with a constant-time comparison. Rotate by
  changing the environment file and restarting the service.
- The content repo is public by requirement. The editor only writes; nothing
  private is involved.

### 2.6 Content conventions

- Any folder depth. Folder names appear in the sidebar as written.
- `README.md` in a folder is that folder's index page. The repo root
  `README.md` is the site home page.
- Markdown files: optional YAML frontmatter with `title`. Title resolution
  order: frontmatter `title`, else first `# H1` (which is then removed from
  the body so it is not rendered twice), else file name without extension.
- Other text files: shown as one code block. Page title is the file name with
  extension. Language is derived from the extension (section 4.1).
- Only `.md` is rendered as Markdown. `.mdx` is treated as a plain text file in
  v1 because untrusted MDX can break the build.
- Files and folders starting with a dot, and the `.github` folder, are ignored.
- Binary files (not valid UTF-8) are skipped with a build warning.

---

## 3. Code repository layout

```
snipcache/
  CLAUDE.md
  README.md
  package.json                 root scripts: dev, build, test
  pnpm-workspace.yaml          packages: site, frontend/apps/*, frontend/shared/*
  vitest.config.ts             one runner for all frontend packages
  global.json                  "test": { "runner": "Microsoft.Testing.Platform" }
  SnipCache.slnx
  .editorconfig
  .github/workflows/
    ci.yml                     build + test on push/PR
    build-site.yml             reusable workflow (workflow_call) used by content repos
  site/                        Astro + Starlight site builder
    astro.config.mjs
    package.json
    scripts/prepare-content.ts
    scripts/prepare-content.test.ts
    scripts/check-build.ts     build smoke test (CI)
    src/content.config.ts
    src/components/PageTitle.astro
    src/styles/custom.css
    sample-content/            small fixture tree used by dev and tests
    src/content/docs/          GENERATED (gitignored)
    public/raw/                GENERATED (gitignored)
    src/generated/sidebar.json GENERATED (gitignored)
  backend/
    shared/SnipCacheAuth/      copy of Cestius' CestiusAuth, namespace SnipCacheAuth
    apps/SnipCacheEditor/      minimal API, OpenAPI emission on build
    tests/SnipCacheAuth.Tests/
    tests/SnipCacheEditor.Tests/
  frontend/
    shared/content-rules/      @snipcache/content-rules: path → slug rule (site + editor)
    shared/snipcache-auth/     copy of @cestius/auth as @snipcache/auth
    apps/editor/               Vite + React + Monaco SPA
  scripts/deploy/              copy of the Cestius deploy orchestrator, app "snipcache"
  doc/
    SnipCache-features.md
    snipcache-architecture-and-plan.md
```

Naming follows the Cestius rules: kebab-case for frontend packages, PascalCase
for .NET projects, one flat namespace per .NET project.

The auth libraries are **copied, not referenced**, because the two repos live
in different places and SnipCache is public. The copies keep the Cestius code
unchanged apart from the rename so they can later be replaced by a published
NuGet and npm package.

---

## 4. Component design

### 4.1 Site builder (`site/`)

**prepare-content** (`site/scripts/prepare-content.ts`, run before `astro dev`
and `astro build`). Input: `SNIPCACHE_CONTENT_DIR` (defaults to
`site/sample-content`). Output: `src/content/docs/`, `public/raw/`,
`src/generated/sidebar.json`. Rules:

| Source | Generated page | Title | Slug |
|--------|----------------|-------|------|
| `a/b/README.md` | `a/b/index.md` | per Markdown rule | `a/b` |
| `README.md` (root) | `index.md` | per Markdown rule | `/` |
| `a/b/note.md` | `a/b/note.md` | frontmatter, H1 or `note` | `a/b/note` |
| `a/b/count.sql` | `a/b/count.sql.md` | `count.sql` | `a/b/count-sql` |

- Slugs are set explicitly in frontmatter so they stay predictable: each path
  segment is lower-cased, umlauts are transliterated (`ä` → `ae`, `ß` → `ss`),
  other accents stripped, dots and whitespace become `-`, everything except
  letters, digits, `-` and `_` is dropped, and runs of `-` collapse. The rule
  lives in `@snipcache/content-rules` (`frontend/shared/content-rules`), which
  the editor reuses for "View on site". Collisions of slugs or generated page
  paths fail the build with a message naming both source files.
- Generated frontmatter: `title`, `slug`, `editUrl` (when `SNIPCACHE_EDITOR_URL`
  is set: `<editor>/edit/<original path>`), and a custom `snipcache` object
  with `sourcePath`, `rawUrl` (including the base path), `kind` (`markdown` or
  `file`) and `language`. From a Markdown file's own frontmatter only `title`
  and a string `description` are carried over, so arbitrary keys cannot break
  the schema validation; invalid YAML is dropped with a warning.
- A `README.md` without frontmatter title or H1 is titled after its folder.
  Without a root `README.md` a minimal home page is generated.
- Code pages have no table of contents.
- Code block pages use a fence longer than any fence inside the file content,
  with `title="<file name>"` so Expressive Code shows the name.
- Language map: `ps1`, `psm1` → powershell; `sh`, `bash`, `zsh` → bash; `bat`,
  `cmd` → batch; `sql`, `json`, `yaml`/`yml`, `xml`, `toml`, `ini`, `py`, `js`,
  `ts`, `cs`, `csproj` → xml, `Dockerfile` → dockerfile, `txt` and unknown →
  text.
- Raw copies: every content file is copied to `public/raw/<original path>` so
  "copy whole file" and "open raw" work for Markdown sources too.
- `sidebar.json`: nested groups mirroring folders. Folders first, then files,
  both sorted case-insensitively (numbers in natural order). A folder with a
  README gets its index page as the group's first entry. Groups start
  collapsed; Starlight opens the one containing the current page. The home
  page is not listed.
- Warnings (skipped binaries, bad frontmatter) are printed as `::warning::`
  annotations when running in GitHub Actions.

**Starlight configuration** (`astro.config.mjs`):

- `site` and `base` from `SNIPCACHE_SITE` and `SNIPCACHE_BASE` (the workflow
  derives them from the repo name; `base` is `/` for `f4b4.github.io` repos
  and `/<repo>` otherwise).
- `title` from `SNIPCACHE_TITLE` (default "SnipCache").
- `sidebar` imported from `sidebar.json`.
- `components: { PageTitle: './src/components/PageTitle.astro' }`.
- `pagefind: true` (default). Search is unavailable in `astro dev`; verify with
  `astro build && astro preview`.
- Content schema: `docsSchema({ extend: z.object({ snipcache: ... }) })` in
  `src/content.config.ts`.

**PageTitle override**: renders the title plus a small toolbar: "Copy file"
(fetches `rawUrl`, writes to clipboard, shows "Copied"), "Raw" (link), "Edit"
(link to `editUrl`, hidden when no editor is configured). Starlight's own
footer "Edit page" link remains and points to the same URL.

**Local development**: `pnpm --filter site dev` runs prepare-content on the
sample tree, then `astro dev`. `SNIPCACHE_CONTENT_DIR=../../snipcache-content-f4b4
pnpm --filter site build` builds the real content locally.

### 4.2 Reusable workflow and template

`snipcache/.github/workflows/build-site.yml`:

```yaml
name: Build and deploy SnipCache site
on:
  workflow_call:
    inputs:
      site_title: { type: string, default: SnipCache }
      editor_url: { type: string, default: '' }
      builder_ref: { type: string, default: v1 }
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { repository: f4b4/snipcache, ref: ${{ inputs.builder_ref }}, path: builder }
      - uses: actions/checkout@v4
        with: { path: content }
      - uses: pnpm/action-setup@v4
        with: { version: 12 }
      - uses: actions/setup-node@v4
        with: { node-version: 24, cache: pnpm, cache-dependency-path: builder/pnpm-lock.yaml }
      - run: pnpm install --frozen-lockfile --filter site...   # site plus its workspace deps
        working-directory: builder
      - run: pnpm --filter site build
        working-directory: builder
        env:
          SNIPCACHE_CONTENT_DIR: ${{ github.workspace }}/content
          SNIPCACHE_SITE: https://${{ github.repository_owner }}.github.io
          SNIPCACHE_BASE: <computed in a prior step from github.event.repository.name>
          SNIPCACHE_TITLE: ${{ inputs.site_title }}
          SNIPCACHE_EDITOR_URL: ${{ inputs.editor_url }}
      - uses: actions/upload-pages-artifact@v3
        with: { path: builder/site/dist }
  deploy:
    needs: build
    permissions: { pages: write, id-token: write }
    environment: { name: github-pages, url: ${{ steps.deployment.outputs.page_url }} }
    runs-on: ubuntu-latest
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

The caller in the template and in every content repo:

```yaml
name: Deploy SnipCache
on:
  push: { branches: [main] }
  workflow_dispatch:
permissions: { contents: read, pages: write, id-token: write }
concurrency: { group: pages, cancel-in-progress: true }
jobs:
  deploy:
    uses: f4b4/snipcache/.github/workflows/build-site.yml@v1
    with:
      site_title: My SnipCache
      editor_url: https://snipcache.freeddns.org
```

`v1` is a moving tag in the code repo that is advanced after each compatible
release. Breaking changes get `v2`. A one-time manual step per content repo:
Settings → Pages → Source → "GitHub Actions".

### 4.3 Editor backend (`backend/apps/SnipCacheEditor`)

**Wiring** (`Program.cs`), mirroring the Cestius showcase: `UseSystemd()`,
Serilog console + rolling file, `AddOpenApi()` with build-time emission for
Orval, `AddSnipCacheAuth(...)`, `UseSnipCacheAuth()`, `MapSnipCacheAuthEndpoints()`.
No database.

**Endpoints** (all under `/api`, cookie auth, `RequireAuthorization`, optional
role policy `snipcache-editor`):

| Method | Route | Request | Response |
|--------|-------|---------|----------|
| GET | `/api/config` | | `{ siteBaseUrl, repository, branch }` |
| GET | `/api/tree` | | `{ nodes: [{ path, type: "dir"\|"file", size }] }` flat list, client builds the tree |
| GET | `/api/files/{**path}` | | `{ path, sha, content, encoding: "utf-8" }`; 404; 413 above 1 MB |
| PUT | `/api/files/{**path}` | `{ content, sha?, message? }` | `{ sha, commitSha, commitUrl }`; 201 on create; 409 when `sha` does not match the current one |
| POST | `/api/folders` | `{ path, title? }` | creates `<path>/README.md` with `# <title or folder name>`; 409 if it exists |

Bot endpoint, API-key auth scheme, outside the CSRF guard:

| Method | Route | Request | Response |
|--------|-------|---------|----------|
| POST | `/bot/snippets` | `{ folder?, fileName, content, message?, overwrite?: false }` | `{ path, sha, commitUrl, pageUrl }`; 201 on create, 200 on overwrite; 409 if the file exists and `overwrite` is false, or its page slug equals an existing file's |

The bot authenticates with `X-Api-Key` (scheme `BotApiKey`, constant-time
comparison of SHA-256 hashes); a cookie session does not count on `/bot`. Its
commits are authored by `Bot:AuthorName` (default `SnipCache Bot`). The page
URL and the slug collision check use `PageSlugs`, a C# port of
`@snipcache/content-rules`; both implementations run the cases in
`content-rules/src/slug-cases.json`. Details: [bot-api.md](bot-api.md).

Validation: paths are normalised (NFC, `\` → `/`), must be relative, must not
contain empty segments, control characters or any segment starting with a dot
(this covers `..` and `.github`). Names the editor **creates** (new file, new
folder, bot) must also match `^[\w .\-()]+$` without surrounding whitespace.
Existing files are exempt from that pattern so content added through git
(e.g. `WSL2, VS Code, Visual Studio.md`) can still be opened and saved. Commit
messages default to `Edit <path>`, `Add <path>`, `Add folder <path>`,
`Bot: add <path>`.

Further status codes: 400 (validation problem with a `path` or `content`
error), 415 for files that are not UTF-8 text, 502 when GitHub fails (a 401 from
GitHub is reported as "access token rejected"). Errors are RFC 9457 problem
details. In Development without `Keycloak:Authority`, `GET /auth/dev-login`
issues a cookie session for a made-up user (curl and SPA work without
Keycloak). `GET /health` is an anonymous health check.

**Content repository service** (`Content/`): interface `IContentRepository`
with `GetTreeAsync`, `GetFileAsync`, `PutFileAsync`, and an Octokit.net
implementation `GitHubContentRepository`:

- Tree: `Git.Tree.GetRecursive(owner, repo, branch)`, cached in `IMemoryCache`
  for 30 seconds and invalidated after every write.
- File: `Repository.Content.GetAllContentsByRef(owner, repo, path, branch)`;
  returns decoded content and SHA.
- Write: `CreateFile` / `UpdateFile` with `Author = (Keycloak display name,
  email)` and `Committer = (GitHub:CommitterName, GitHub:CommitterEmail)`,
  default `SnipCache` / `noreply@snipcache.freeddns.org`. The author name is
  `name`, else `preferred_username`; without an `email` claim the committer
  address is used. Conflicts map to HTTP 409: a create on an existing path
  (GitHub 422), a stale SHA (GitHub 409) and an update of a deleted file
  (404/422).
- An in-memory fake implements the same interface for tests and for local
  development without GitHub access (`GitHub:UseFake=true`).

**Configuration keys** (in addition to the auth library's keys):

| Key | Example | Source |
|-----|---------|--------|
| `GitHub:Owner` | `f4b4` | appsettings |
| `GitHub:Repository` | `snipcache-content-f4b4` | appsettings |
| `GitHub:Branch` | `main` | appsettings |
| `GitHub:Token` | fine-grained PAT | environment file `GitHub__Token` |
| `GitHub:UseFake` | `true` in dev without token | appsettings.Development |
| `Site:BaseUrl` | `https://f4b4.github.io/snipcache-content-f4b4` | appsettings |
| `Bot:ApiKey` | random 32+ bytes, base64 | environment file `Bot__ApiKey` |
| `Auth:RequiredRole` | `snipcache-editor` or empty | appsettings |
| `Keycloak:Authority` | `https://auth-colmena.freeddns.org/realms/<realm>` | appsettings |
| `Keycloak:ClientId` | `snipcache` | appsettings |
| `App:BaseAddress` | `https://snipcache.freeddns.org` | appsettings |
| `Auth:CookieName` | `snipcache-auth` | appsettings |
| `DataProtection:ApplicationName` / `KeysPath` | `snipcache` / `/var/lib/snipcache/data/dp-keys` | appsettings |

**Tests** (`SnipCacheEditor.Tests`, xunit v3 on Microsoft.Testing.Platform):
path validation, title and message defaults, 409 mapping, endpoint tests with
`WebApplicationFactory`, the fake repository and a test authentication handler.

### 4.4 Editor frontend (`frontend/apps/editor`)

- Vite, React 19, TypeScript, react-router 7, TanStack Query, Orval-generated
  hooks routed through `@snipcache/auth`'s `apiFetch` (never raw `fetch`),
  Tailwind v4 with a small token set, `@monaco-editor/react` with Monaco
  bundled locally (no CDN loader).
- Routes: `/` (tree, empty editor, hint), `/edit/*` (deep link target),
  `/home` (sign-in landing), `/logged-out`, `/unauthorized`.
- Layout: left pane tree with expand/collapse, "New file" and "New folder"
  buttons; right pane Monaco with file name, dirty marker, Save button, link
  "View on site" built from `siteBaseUrl` and the same slug rule as
  prepare-content (`pageUrl` from the workspace package
  `@snipcache/content-rules`, created in phase 1).
- Monaco language from extension: markdown, powershell, shell, bat, sql, json,
  yaml, xml, plaintext.
- Save: Ctrl+S or button → `PUT` with the loaded SHA. On 409 show a dialog:
  "This file changed on GitHub since you opened it" with "Reload" (discard) and
  "Keep editing" (user copies their text out). A merge view is backlog.
- Dialogs: new file (parent folder prefilled from the selected node, name with
  extension, optional title that seeds a `# Title` line for `.md`), new folder
  (parent, name, optional title). Both refresh the tree and open the new file.
- `beforeunload` guard while dirty. Tree and file queries invalidate after
  every mutation.
- Tests with Vitest and React Testing Library: tree rendering from a flat
  list, dialogs' validation, save success and 409 flow with mocked hooks.
- Dev: HTTPS with mkcert, proxy `/api`, `/auth` and `/bot` to the Kestrel port,
  same as the Cestius showcase.

Implementation notes (phase 4):

- Sign-in: `ProtectedRoute` sends visitors without a session to `/home`
  with the wanted page as `returnUrl`; `/home` then starts the login right
  away (an edit link from the site goes straight to Keycloak and back), unless
  it already did so in the last 30 seconds, which prevents a redirect loop
  when the cookie does not stick. Opened directly, `/home` shows a button.
- A 403 from the API (user lacks `Auth:RequiredRole`) leads to
  `/unauthorized`; the SPA does not know the role itself.
- New file and new folder dialogs check the backend's name rules, existing
  paths and slug collisions with existing pages (which would fail the site
  build) before calling the API. New files need an extension.
- The open file comes from the URL (`/edit/<path>`, segments encoded); paths
  are encoded per segment before they go into generated API URLs.
- Monaco is uncontrolled: the pane keeps the draft and remounts the editor
  only when it replaces the text (reload after a conflict). Navigating to
  another file with unsaved edits asks for confirmation (`useBlocker`).
- Without Keycloak, Development maps stand-ins for `/auth/login`, `/auth/me`
  and `/auth/logout` in the editor backend, so the SPA works unchanged
  against the fake repository.

### 4.5 Deployment

Copy `scripts/deploy` from Cestius and add `apps/snipcache.ps1` plus the
`apps/snipcache/` assets:

- `nginx.conf`: server `snipcache.freeddns.org`, TLS via certbot webroot, `/`
  serves `/var/www/snipcache`, `/api`, `/auth` and `/bot` proxy to Kestrel on a
  local port, SPA fallback to `index.html`.
- `snipcache.service`: `Type=notify`, `EnvironmentFile=/etc/snipcache/secrets.env`
  (holds `GitHub__Token` and `Bot__ApiKey`, mode 600, owner root, group
  `snipcache`), `WorkingDirectory=/opt/snipcache`.
- `appsettings.Production.json`: all non-secret keys from the table above.
- Server paths: `/opt/snipcache` (backend), `/var/www/snipcache` (SPA),
  `/var/lib/snipcache/data` (logs, Data Protection keys).

Docker is not needed for v1. If the server moves to Docker later, the same
image can wrap the published output; the configuration surface stays identical.

---

## 5. Manual setup checklist

**GitHub**

1. Create public repo `snipcache`, add it as remote of this folder, push.
2. Create public repo `snipcache-template`; after phase 2 mark it as a template
   (Settings → General → Template repository) and set Pages source to
   "GitHub Actions".
3. Create the content repo from the template; set Pages source to "GitHub
   Actions"; run "Deploy SnipCache" once via `workflow_dispatch`.
4. Create a fine-grained PAT: resource owner = you, repository access = the
   content repo only, permission Contents: Read and write. Note the expiry date
   in a calendar; rotation is one line in the secrets file.

**Keycloak** (`https://auth-colmena.freeddns.org`)

1. Choose the realm (existing `cestius` realm or a new one).
2. Client `snipcache`: public, Standard flow, PKCE S256, client authentication
   off. Valid redirect URIs: `https://snipcache.freeddns.org/auth/*` and
   `https://localhost:5173/auth/*` for dev. Post-logout redirect:
   `https://snipcache.freeddns.org/logged-out`. Web origins: both origins.
3. Optional: role `snipcache-editor` and a mapper that puts client roles into
   the `roles` claim **of the ID token** (the BFF reads claims from the ID token
   and maps only standard claims from userinfo); assign to your user and set
   `Auth:RequiredRole`.

**Server**

1. Point `snipcache.freeddns.org` at the server (dynamic DNS client).
2. `./scripts/deploy/deploy.ps1 snipcache -Setup` once, then certbot for the
   domain, then write `/etc/snipcache/secrets.env`.
3. `./scripts/deploy/deploy.ps1 snipcache` for every release.

---

## 6. Implementation phases

Each phase ends in a working, verifiable state. Phases 1 and 3 are independent
and can run in parallel. Tests and CI are part of each phase, not a final step.

### Phase 0: Repository bootstrap

1. Rename references from SnipCache to SnipCache in `CLAUDE.md`, the
   workspace file and README. Keep `doc/SnipCache-features.md` as the
   historical spec.
2. Create the monorepo skeleton: `package.json`, `pnpm-workspace.yaml`,
   `vitest.config.ts`, `global.json`, `SnipCache.slnx`, `.editorconfig`,
   `.gitignore` additions (`site/src/content/docs/`, `site/public/raw/`,
   `site/src/generated/`, `*.pem`, `.dp-keys/`).
3. `ci.yml`: pnpm install, `pnpm -r build`, `pnpm test`, `dotnet build`,
   `dotnet test`.
4. Update `CLAUDE.md` with the commands as they appear.

Done when: CI is green on an empty skeleton and the repo is pushed to GitHub.

### Phase 1: Site builder

1. Scaffold `site/` with Astro and Starlight, pinned minor versions.
2. Implement `prepare-content.ts` with the rules in 4.1 and unit tests for:
   title resolution and H1 removal, README to index, slug generation and
   collision detection, fence length, language map, dotfile and binary
   skipping, sidebar ordering.
3. Add `sample-content/` with one of each: nested folders, a README, Markdown
   with and without H1 and frontmatter, `.ps1`, `.sh`, `.bat`, `.sql`, a
   Markdown table, a file whose content contains triple backticks.
4. Content schema extension and `PageTitle.astro` with copy, raw and edit
   actions.
5. `astro.config.mjs` reading the environment variables; `base` handling
   verified with a non-root base.
6. Scripts: `dev` (prepare + astro dev), `build` (prepare + astro build),
   `preview`, `test`.
7. Build smoke test in CI: build the sample tree, assert the `pagefind/`
   directory, a generated code page and the raw copies exist.

Done when: `pnpm --filter site build && pnpm --filter site preview` shows the
sample tree with working search, copy buttons and edit links, under a non-root
base path.

### Phase 2: Reusable workflow, template and first real deployment

1. Add `build-site.yml` (workflow_call) with base and site derivation.
2. Create `snipcache-template` with README, example folders, and
   `.github/workflows/deploy.yml` named "Deploy SnipCache" pinned to `@main`
   until `v1` exists.
3. Deploy the template repo itself once to verify Pages works from a fresh
   repo. Mark it as a template.
4. Create the owner's content repo from the template, migrate the real
   snippets, deploy.
5. Tag `v1` in `snipcache`, switch the callers to `@v1`.
6. Write `doc/content-repo-guide.md`: how to create an instance, conventions,
   how to configure the editor link.

Done when: a push to the content repo updates the live site within a few
minutes and search finds a newly added snippet.

### Phase 3: Editor backend

1. Copy `CestiusAuth` to `backend/shared/SnipCacheAuth` (rename namespace,
   defaults, config keys unchanged), copy its tests.
2. Create `SnipCacheEditor` with logging, OpenAPI emission, auth wiring, the
   `/api/config` endpoint and a health endpoint.
3. `IContentRepository`, `GitHubContentRepository` (Octokit.net), the in-memory
   fake, path validation, commit message defaults.
4. Tree, file get, file put, folder create endpoints with 404, 409, 413
   mapping; role policy from `Auth:RequiredRole`.
5. Tests: validation, fake-backed endpoint tests, SHA conflict, author mapping.
6. Manual check against the real content repo with a PAT in user-secrets:
   read tree, read file, write file, confirm the commit and the triggered
   deploy.

Done when: `dotnet test` is green and a `curl` with a dev cookie session can
read and write the real repo.

### Phase 4: Editor frontend

1. Copy `@cestius/auth` to `frontend/shared/snipcache-auth` as
   `@snipcache/auth`, with tests.
2. Scaffold `frontend/apps/editor`: Vite HTTPS dev with proxies, Tailwind,
   router, `AuthProvider`, `ProtectedRoute`, Orval config generating hooks from
   `backend/apps/SnipCacheEditor/openapi/openapi.json`.
3. Tree pane from the flat node list; selection; expand state.
4. Monaco editor pane with language detection, dirty state, Ctrl+S, save
   mutation, 409 dialog, "View on site" link.
5. New file and new folder dialogs.
6. Deep link `/edit/*` and the login return flow.
7. Tests as listed in 4.4.

Done when: the full edit flow works locally against the fake repository and
against the real repo, including login through the real Keycloak with the dev
redirect URI.

### Phase 5: Bot API

1. API-key authentication handler and `Bot:ApiKey` option.
2. `POST /bot/snippets` on the shared commit service, with `overwrite`
   handling and the `pageUrl` in the response.
3. Tests: missing key, wrong key, create, conflict, overwrite.
4. `doc/bot-api.md` with a `curl` example and a note on how a chat assistant
   can be told to "save the essence of this chat to SnipCache" through a
   custom tool or HTTP action.

Done when: a `curl` call creates a file in the content repo and the page
appears on the site after the deploy.

### Phase 6: Deployment to snipcache.freeddns.org

1. Copy `scripts/deploy` from Cestius, add the `snipcache` app config and
   assets (nginx site, systemd unit, `appsettings.Production.json`).
2. Keycloak client and optional role (section 5).
3. Server provisioning with `-Setup`, certbot, secrets file.
4. First deploy; set `editor_url` in the content repo workflow; redeploy the
   site so the edit links point at the editor.
5. End-to-end check: open a page on GitHub Pages, click Edit, sign in, change
   the file, save, wait for the deploy, reload the page, verify the change and
   the commit author.
6. Update `CLAUDE.md` and `README.md` with run, test and deploy commands.

Done when: the end-to-end check passes and the checklist in section 5 is
fully ticked.

---

## 7. Risks and open points

- **Starlight minor releases change APIs.** Pin exact minor versions in
  `site/package.json`; upgrade deliberately and re-run the build smoke test.
- **Slug surprises.** Spaces, umlauts and dots in file names map to slugs that
  differ from the file name. The explicit slug rule and the collision check
  make this deterministic; the "View on site" link relies on the same rule in
  the editor, so the rule must live in one shared module. The bot's page URL
  needs it in the backend too; that C# port is held to the TS module by a
  shared file of test cases.
- **H1 removal.** A Markdown file whose first H1 is not meant as the title
  will lose that heading in the rendered page. Setting `title` in frontmatter
  disables the removal.
- **Mixed German and English content.** Pagefind stems per page language. The
  site declares `lang="en"`; German words still match on exact terms. Per-file
  `lang` frontmatter can be added later.
- **GitHub API rate limit** is 5,000 requests per hour per token; the 30-second
  tree cache keeps the editor well below it. Files above 1 MB are rejected in
  v1 because the Contents API does not return their content.
- **PAT expiry** silently breaks saves. Log a warning at startup when the token
  is within 14 days of its configured expiry date (`GitHub:TokenExpires`,
  optional) and surface GitHub 401 as a clear error in the editor.
- **Dynamic DNS and certbot.** Certificate renewal needs port 80 reachable;
  this is already proven for the Cestius apps on the same server.
- **Node version.** Node is only a build tool (static site and SPA); nothing
  runs on Node in production, so there is no runtime version to match. The
  floor is Node 22.12 (Astro 6, pnpm 11), declared in `engines`. Local
  development and `ci.yml` use any even-numbered LTS (`lts/*`). Only the
  reusable `build-site.yml` pins a major (currently 24), because it runs in
  other people's content repos and must not jump to a new LTS before the
  builder has been tested on it; raise it deliberately.
- **Reusable workflow trust.** Content repos run code from `snipcache@v1`.
  Only the owner can move the tag, and the workflow runs with the caller's
  Pages permissions only.

---

## 8. Backlog (after v1)

- MCP server on the editor with Keycloak as OAuth provider: tools
  `save_snippet`, `read_snippet`, `list_tree`; later `search` once a server
  index exists.
- GitHub App authentication instead of a PAT.
- Rename, move and delete in the editor; Markdown preview pane; merge view
  on conflicts.
- Tags in frontmatter with a tag index page; images and attachments next to
  Markdown files.
- Custom domain for the Pages site (`CNAME` input on the workflow).
- Server-side search (Lucene.NET) if a bot search API becomes necessary.
- Per-file `lang` frontmatter for better Pagefind stemming.
- Docker packaging of the editor.
