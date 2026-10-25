# Content repo guide

How to run your own SnipCache instance: a GitHub repository of snippet files
that publishes itself as a searchable website on GitHub Pages.

Audience: anyone who wants a SnipCache collection. No build tools are needed;
everything runs in GitHub Actions.

## Create an instance

1. Open [f4b4/snipcache-template](https://github.com/f4b4/snipcache-template)
   and choose **Use this template → Create a new repository**. The repository
   must be public unless your GitHub plan offers Pages for private repos.
2. In the new repository, open **Settings → Pages** and set **Source** to
   **GitHub Actions**.
3. Open **Actions → Deploy SnipCache → Run workflow**. The first run usually
   takes one to two minutes. (The run started by the initial commit fails if
   step 2 was not done yet; this run replaces it.)
4. The site is at `https://<owner>.github.io/<repository>/`. A repository
   named `<owner>.github.io` is served at `https://<owner>.github.io/`.

From then on every push to `main` rebuilds the site and its search index. A
newer push cancels a build that is still running, so the last push wins.

Replace the example folders and the `README.md` with your own content.

## Content conventions

| File | Becomes |
|------|---------|
| `README.md` in the root | the home page |
| `README.md` in a folder | the folder's index page (first entry of its sidebar group) |
| any other `.md` file | a rendered Markdown page |
| any other text file (`.sql`, `.ps1`, `.sh`, `.bat`, `.json`, …) | a page with the whole file in one highlighted code block |

- **Folders** become sidebar groups, nested to any depth. Folders come
  first, then files, sorted case-insensitively with numbers in natural order.
- **Markdown titles**: frontmatter `title`, else the first `# Heading` (which is
  then not repeated in the body), else the file name without extension. Set a
  `title` in frontmatter if the first heading is not meant as the page title.
  A folder `README.md` without a title is named after the folder.
- **Frontmatter**: only `title` and `description` are used; other keys are
  ignored. Invalid YAML is ignored with a warning in the build log.
- **Other files** are titled with their full file name (`Row counts.sql`).
  The highlighting language follows the extension: `ps1`/`psm1` PowerShell,
  `sh`/`bash`/`zsh` shell, `bat`/`cmd` batch, `sql`, `json`, `yaml`, `xml`,
  `toml`, `ini`, `py`, `js`, `ts`, `cs`, `csproj`, `Dockerfile`; anything else
  is plain text.
- **`.mdx` files** are shown as plain text, not rendered.
- **Ignored**: files and folders whose name starts with a dot (including
  `.github`), and binary files (logged as a build warning). Images are not
  supported yet.
- **Page addresses** are derived from the path: lower case, umlauts
  transliterated (`ä` → `ae`, `ß` → `ss`), dots and spaces turned into `-`,
  other punctuation dropped. `Scripts/Row counts.sql` becomes
  `/scripts/row-counts-sql/`. Two files that map to the same address fail the
  build with a message naming both; rename one of them.

Every page has a **Copy file** button (copies the whole source file) and a
**Raw** link; code blocks have their own copy button. Search covers all pages.

## Configure the workflow

`.github/workflows/deploy.yml` in the content repo calls the reusable workflow
from this repository:

```yaml
jobs:
  deploy:
    uses: f4b4/snipcache/.github/workflows/build-site.yml@v1
    with:
      site_title: SnipCache
      editor_url: ''
```

| Input | Default | Meaning |
|-------|---------|---------|
| `site_title` | `SnipCache` | Title in the header and browser tab. |
| `editor_url` | empty | Origin of a SnipCache editor, e.g. `https://snipcache.freeddns.org`. When set, every page gets an **Edit** button and an "Edit page" footer link to `<editor_url>/edit/<file path>`. Empty hides them. |
| `builder_ref` | `v1` | Ref of `f4b4/snipcache` the site is built with. Leave it at the default; if you change the `@v1` in `uses:`, change this to the same ref. |

`v1` is a moving tag that receives compatible improvements, so content repos
pick them up without changes. A breaking change gets a new major tag (`v2`),
which you opt into by changing both `@v1` and `builder_ref`.

## Connect the editor

The editor (`https://snipcache.freeddns.org` for the owner's instance) writes
to one content repo through the GitHub API, one commit per save; every save
triggers "Deploy SnipCache". To link a site to it, set `editor_url` as above
and push. The editor itself is configured with the content repo's owner and
name and a fine-grained personal access token with **Contents: read and write**
on that repository only; see section 4.3 of
[snipcache-architecture-and-plan.md](snipcache-architecture-and-plan.md).

## Troubleshooting

- **Deploy job fails with "Pages not enabled" or HTTP 404**: set Settings →
  Pages → Source to "GitHub Actions", then re-run the workflow.
- **The site shows the plain README instead of SnipCache**, or the Deploy job
  fails with "Creating Pages deployment failed": the Pages source was reset to
  "Deploy from a branch". This happens when the repository's visibility is
  changed (private and back to public). Set Settings → Pages → Source to
  "GitHub Actions" again and re-run "Deploy SnipCache".
- **Deploy fails with a branch protection error**: Settings → Environments →
  `github-pages` must allow deployments from `main`.
- **Build fails with a slug collision**: two files map to the same page address
  (for example `Notes.md` and `notes.md`, or `a b.md` and `a-b.md`). Rename one.
- **A page is missing**: check the build log for warnings about binary files
  or files ignored because their name starts with a dot.
- **Build locally**: clone this repository next to the content repo and run
  `SNIPCACHE_CONTENT_DIR=../../<content-repo> pnpm --filter site build`, then
  `pnpm --filter site preview` (search only works in the preview, not in
  `dev`).
