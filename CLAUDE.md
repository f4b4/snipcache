# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**SnipCache** (a "knowledge store") is in the
**planning phase**. The design is approved and documented; implementation has not
started. The repository is a local git repo with one commit and no remote yet.

Documents:

- [doc/snipcache-architecture-and-plan.md](doc/snipcache-architecture-and-plan.md) —
  **source of truth**: decisions, architecture, component design, phased plan.
  Read it before implementing anything.
- [doc/SnipCache-features.md](doc/SnipCache-features.md) — the original
  concept and feature list (historical; the plan supersedes its open questions).
- `SnipCache.code-workspace` — VS Code workspace.

When implementing a phase, follow the plan's layout and update this file with the
build, test and run commands as they come into existence.

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
- Runtime: .NET 10, Node 22, pnpm, React 19, TypeScript.
- v1 editor scope: open, edit, save, new file, new folder. Rename/move/delete,
  preview, tags, images, MCP server and GitHub App auth are backlog.

## Conventions

- Documentation in English; the project name and some terms may be German.
- Naming: kebab-case for frontend packages and modules, PascalCase for .NET
  projects with one flat namespace per project (same as Cestius).
- Secrets (GitHub token, bot API key) never go into `appsettings*.json`; they are
  loaded from an environment file in production and user-secrets in development.
