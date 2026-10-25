# SnipCache

A knowledge store for know-how snippets: Markdown recipes and how-tos, SQL,
PowerShell/batch/shell scripts, Markdown tables and knowledge captured from AI
chats. The snippets are plain files in a GitHub repo; every push publishes a
searchable static site to GitHub Pages, and a small web editor writes to the repo
through the GitHub API.

This monorepo contains:

- `site/`: Astro + Starlight site builder with a Pagefind search index
- `.github/workflows/build-site.yml`: reusable workflow called by content repos
- `backend/` and `frontend/`: the editor (ASP.NET Core minimal API, React + Monaco)

To start your own collection, create a repo from the `snipcache-template`
repository.

See [doc/snipcache-architecture-and-plan.md](doc/snipcache-architecture-and-plan.md)
for the architecture and implementation plan.

## Development

Requirements: Node 22.12 or newer (any even-numbered LTS), pnpm 12, .NET 10 SDK.

```sh
pnpm install
pnpm build        # build all site and frontend packages
pnpm test         # run all Vitest tests
dotnet build SnipCache.slnx
dotnet test --solution SnipCache.slnx
```
