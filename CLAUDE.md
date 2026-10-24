# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

Wissensspeicher ("knowledge store") is in the **concept phase**. There is no source code, build system, test suite, or git repository yet. The only content is:

- [doc/wissensspeicher-features.md](doc/wissensspeicher-features.md) — the feature specification (source of truth for requirements)
- `Wissensspeicher.code-workspace` — VS Code workspace with default settings

When implementing, read the feature spec first and update this file with build/test/run commands once they exist.

## Intended purpose

A system to collect, edit and search know-how snippets: Markdown recipes/how-tos, SQL snippets, PowerShell/batch/shell scripts, Markdown tables (e.g. keyboard shortcuts), and knowledge captured from AI chats.

## Planned architecture (two repositories)

1. **Wissensspeicher** (content repo)
   - Public GitHub repo holding the snippets as a folder tree of text files (mostly `.md`, also `.ps1`, `.sh`, `.bat`, etc.).
   - Must be usable as a GitHub template so others can create their own instance.
   - Every push triggers a GitHub Actions workflow named **"Deploy Wissensspeicher"**, which publishes the content to GitHub Pages and rebuilds the search engine index.

2. **WissensspeicherEditor** (web editor)
   - Protected by Keycloak authentication.
   - Shows the Wissensspeicher folder/file tree next to a Monaco editor.
   - Folders and new files are created via a button + dialog.
   - Saving a file commits and pushes the change to the Wissensspeicher repo (which in turn triggers the deploy action).

Not yet decided (per the spec): tech stack for the editor, the static site generator for GitHub Pages, and the search engine / index format. Confirm these with the user before choosing.

## Conventions

- The spec is written in English; the project name and some terms are German. Keep documentation in English unless asked otherwise.
