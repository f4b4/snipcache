# Bot API

One endpoint that lets a script, an agent or a chat assistant save a snippet to
the content repository without signing in: `POST /bot/snippets` with an API
key. Each call is one commit, which starts "Deploy SnipCache"; the page is on
the site one to two minutes later.

Audience: whoever wires a bot or assistant to SnipCache. Design background is
in [snipcache-architecture-and-plan.md](snipcache-architecture-and-plan.md),
section 4.3.

## Setup

The key is a secret shared by the editor and the bot. Generate one with at
least 32 characters:

```sh
openssl rand -base64 32
```

- Production: `Bot__ApiKey=<key>` in the editor's environment file, then
  restart the service. Rotate the same way; the old key stops working at once.
- Development: `dotnet user-secrets set Bot:ApiKey <key> --project
  backend/apps/SnipCacheEditor`.

Without a key every `/bot` call gets 401 (the editor logs a warning at
startup). A key shorter than 32 characters stops the editor from starting.

## Request

```
POST https://snipcache.freeddns.org/bot/snippets
X-Api-Key: <key>
Content-Type: application/json
```

| Field | Required | Meaning |
|-------|----------|---------|
| `fileName` | yes | File name with extension, e.g. `Docker aufräumen.md`. Only `.md` is rendered as Markdown; `.sql`, `.ps1`, `.sh` and other text files become a code page. |
| `content` | yes | The whole file, UTF-8 text, at most 1 MB. |
| `folder` | no | Folder path such as `KI-Chats` or `SQL/Postgres`; empty or missing means the repository root. Missing folders are created. |
| `message` | no | Commit message; default `Bot: add <path>` or `Bot: update <path>`. |
| `overwrite` | no | `true` replaces an existing file. Default `false`: an existing file is a 409. |

Names that are created (the file and any new folder) may contain only
letters, digits, spaces and `_ . - ( )`, need at least one letter or digit, and
must not start with a dot. Existing folders and files keep whatever name they
have. A new file whose page address would equal that of an existing file (for
example `SQL/Count.sql` next to `SQL/count.sql`) is refused, because it would
fail the site build.

## Response

`201 Created` for a new file, `200 OK` for an overwrite:

```json
{
  "path": "KI-Chats/Docker aufräumen.md",
  "sha": "f58e2bfdeee4bdb87ed836ea7ba5a7dc52f585a7",
  "commitUrl": "https://github.com/f4b4/snipcache-content-f4b4/commit/f59858a…",
  "pageUrl": "https://f4b4.github.io/snipcache-content-f4b4/ki-chats/docker-aufraeumen/"
}
```

`pageUrl` (also the `Location` header of a 201) is where the page appears once
the deploy has run. Errors are RFC 9457 problem details; `detail` or `errors`
says what to fix:

| Status | When |
|--------|------|
| 400 | Invalid `folder` or `fileName` (key in `errors` names the field), missing `content`, malformed JSON |
| 401 | Missing or wrong `X-Api-Key`, or no key configured |
| 409 | File exists and `overwrite` is false; same page address as another file; a folder in the path is a file, or the path is a folder; the file changed on GitHub during the save |
| 413 | `content` above 1 MB |
| 413, 415 | The existing file at that path is above 1 MB or not UTF-8 text |
| 502 | GitHub failed or rejected the editor's token |

The commit author is `SnipCache Bot` (`Bot:AuthorName`), the committer the
usual SnipCache identity.

## curl

```sh
export SNIPCACHE_BOT_KEY=...   # the key from the environment file

curl -sS https://snipcache.freeddns.org/bot/snippets \
  -H "X-Api-Key: $SNIPCACHE_BOT_KEY" \
  -H 'Content-Type: application/json' \
  --data-binary @- <<'EOF'
{
  "folder": "KI-Chats",
  "fileName": "Docker aufräumen.md",
  "content": "# Docker aufräumen\n\n```sh\ndocker system prune -a --volumes\n```\n",
  "message": "Bot: Docker cleanup from a chat"
}
EOF
```

Locally, against the fake repository, use `https://localhost:7240` and `-k`.
To send a file as it is, let `jq` build the body:

```sh
jq -n --arg folder SQL --arg fileName count.sql --rawfile content count.sql \
  '{folder: $folder, fileName: $fileName, content: $content}' |
  curl -sS https://snipcache.freeddns.org/bot/snippets \
    -H "X-Api-Key: $SNIPCACHE_BOT_KEY" -H 'Content-Type: application/json' --data-binary @-
```

## "Save the essence of this chat to SnipCache"

The assistant needs two things: a way to call the endpoint (a tool or HTTP
action holding the key, so the key never appears in the chat) and an
instruction telling it what a good snippet looks like. A suggested
instruction, to put into the tool description or the assistant's custom
instructions:

> When I say "save this to SnipCache", distil the conversation into one
> self-contained Markdown snippet: an `# H1` title naming the task, a one or
> two sentence summary, then the final working commands, code or steps in
> fenced code blocks with a language. Leave out dead ends, chit-chat and
> anything secret (passwords, tokens, internal host names). Save it with the
> SnipCache tool: folder `KI-Chats` unless I name another, `fileName` is the
> title plus `.md`, using only letters, digits, spaces and `_ . - ( )`. If the
> tool answers 409 because the file exists, ask me whether to overwrite or
> pick a new name. Reply with the `pageUrl`.

Ways to give the assistant the tool:

- **Agents that can run commands** (Claude Code, scripts): keep the key in
  `SNIPCACHE_BOT_KEY` and let the agent run the `jq` + `curl` command above;
  the instruction can go into a `CLAUDE.md` or a skill.
- **Function calling through an API** (Claude, OpenAI and similar): declare a
  tool and let your code forward its input to the endpoint with the key
  added. The input schema is the request body:

  ```json
  {
    "name": "save_snippet",
    "description": "Save a snippet (Markdown or a script) to the user's SnipCache knowledge store.",
    "input_schema": {
      "type": "object",
      "properties": {
        "folder": { "type": "string", "description": "Folder path, e.g. KI-Chats" },
        "fileName": { "type": "string", "description": "File name with extension, e.g. Docker cleanup.md" },
        "content": { "type": "string", "description": "The whole file content" },
        "overwrite": { "type": "boolean", "description": "Replace an existing file" }
      },
      "required": ["fileName", "content"]
    }
  }
  ```

- **HTTP actions configured in a chat product** (for example a custom GPT
  action): import an OpenAPI description of the endpoint and set the
  authentication to an API key sent in the custom header `X-Api-Key`. The
  editor's generated description is in
  `backend/apps/SnipCacheEditor/openapi/openapi.json` (path `/bot/snippets`,
  schemas `SaveSnippetRequest` and `SaveSnippetResponse`); copy that path and
  its schemas, add `servers: [{ url: "https://snipcache.freeddns.org" }]` and
  an `apiKey` security scheme in the `X-Api-Key` header.

Chat products that only accept MCP servers need the MCP server from the
backlog; it is not part of v1.
