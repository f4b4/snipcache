# Deploy

One reusable PowerShell orchestrator that deploys any Cestius app to its Linux VPS
(ssh alias `tank`): a framework-dependent `dotnet publish` plus the built SPA,
served by a **systemd**-managed Kestrel behind **nginx**, **HTTPS-only** (port 80
serves only the ACME challenge and redirects to HTTPS — ready for `.app` domains,
which the HSTS preload list forces onto HTTPS). The committed `nginx.conf` owns the
TLS config; `certbot certonly --webroot` only issues the cert. Showcase has no
database, so there is **no migration step and no secrets file** — all prod config
ships in `appsettings.Production.json`.

```
scripts/deploy/
  deploy.ps1                 PowerShell orchestrator (build -> scp -> invoke bash)
  lib/                       reusable, app-agnostic server-side bash (source deploy.env)
    setup.sh                 first-time: user, dirs, perms, install nginx + unit
    install.sh               routine: converge nginx -> stop -> rsync server/client -> restart -> healthcheck
    config.sh                config-only: reinstall nginx + unit, reload (no build)
  apps/
    showcase.ps1             per-app VARIABLES (host, domain, port, build paths)
    showcase/                per-app server assets
      nginx.conf             -> /etc/nginx/sites-available/showcase
      showcase.service       -> /etc/systemd/system/showcase.service
      appsettings.Production.json   non-secret prod config (overlaid onto the publish)
```

**How it works.** `deploy.ps1 <app>` dot-sources `apps/<app>.ps1` for its variables,
builds locally, writes a `deploy.env` from those variables, `scp`s the artifacts +
per-app assets + `deploy.env` to `~/<app>-upload/`, then streams the matching
`lib/*.sh` over SSH. Each bash script `source`s `deploy.env`, so it is fully
app-agnostic — the only app-specific things on the server are the uploaded files.

## Layout on the server

| Path                              | Purpose                              | Owner      |
| --------------------------------- | ------------------------------------ | ---------- |
| `/opt/showcase`                   | published backend (`Showcase.dll`)   | `showcase` |
| `/var/www/showcase`               | built SPA (Vite `dist`)              | `www-data` |
| `/var/lib/showcase/data/dp-keys`  | DataProtection keys (logins persist) | `showcase` |

Kestrel listens on `127.0.0.1:5003` (Blondi=5002, verby=5001); nginx terminates TLS
and proxies `/api/` and `/auth/` to it.

## Prerequisites (server, once)

- .NET 10 ASP.NET Core runtime, `nginx`, `certbot`. (The `python3-certbot-nginx`
  plugin is **not** needed — we use `certbot certonly --webroot`, so certbot only
  issues the cert and never edits nginx config.)
- The `showcase` public PKCE client exists in the `cestius` realm on
  `auth-colmena.freeddns.org` (it does — used in dev too); add the prod URIs below.

## First-time setup

1. **DNS:** point `showcase.freeddns.org` at the host.
2. **Keycloak** (`cestius` realm, `showcase` client) — add:
   - Valid redirect URI: `https://showcase.freeddns.org/*`
   - Web origin: `https://showcase.freeddns.org`
   - Valid post-logout redirect URI: `https://showcase.freeddns.org/*`
3. **Provision + first deploy:**
   ```powershell
   ./scripts/deploy/deploy.ps1 showcase -Setup
   ```
   This creates the service user/dirs, installs the nginx site (HTTP-only) and the
   systemd unit, then installs the freshly-built backend + SPA and starts the service.
4. **Enable HTTPS** (after DNS resolves here). The committed `nginx.conf` is
   HTTPS-only and owns the TLS config; certbot only issues the cert:
   ```powershell
   ssh tank "sudo certbot certonly --webroot -w /var/www/certbot -d showcase.freeddns.org"
   ./scripts/deploy/deploy.ps1 showcase -Setup
   ```
   `certonly` writes only to `/etc/letsencrypt` (never nginx config); the re-run of
   `-Setup` swaps the temporary HTTP bootstrap for the committed HTTPS config and
   reloads. Renewals reload nginx automatically via the deploy hook setup installs.

   > Because TLS lives in the committed `nginx.conf`, there is nothing to copy back
   > and a later `-ConfigOnly` re-applies it safely — it never reverts HTTPS.

## Routine deploys

```powershell
./scripts/deploy/deploy.ps1 showcase             # client + server
./scripts/deploy/deploy.ps1 showcase -ServerOnly
./scripts/deploy/deploy.ps1 showcase -ClientOnly
./scripts/deploy/deploy.ps1 showcase -ConfigOnly # re-push nginx + unit, reload
```

There's also a root-level `pnpm` shortcut (just forwards to the script above):

```sh
pnpm deploy:showcase                # client + server
pnpm deploy:showcase -- -ServerOnly # forward flags after `--`
```

Each server deploy re-applies the committed `nginx.conf` (validated with `nginx -t`
before the service is touched, and skipped on a cert-less box where the HTTP
bootstrap must stay), stops the service, rsyncs the new binaries, refreshes the
unit, then restarts, reloads nginx, and health-checks. No migrations (no database).
`-ConfigOnly` is still useful when you want to push only the nginx/unit config
without a build.

## Onboarding another app

Copy `apps/showcase.ps1` to `apps/<app>.ps1` and adjust the variables, then create
`apps/<app>/` with its own `nginx.conf`, `<app>.service`, and
`appsettings.Production.json`. `deploy.ps1` and everything in `lib/` stay untouched.
Give the new app a free loopback port and its own Keycloak client URIs. Optionally
add a `deploy:<app>` shortcut to the root `package.json` (mirrors `deploy:showcase`).
