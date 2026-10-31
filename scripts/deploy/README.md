# Deploy

One reusable PowerShell orchestrator (copied from the Cestius repo) that deploys
the SnipCache editor to the Linux server (ssh alias `tank`): a framework-dependent
`dotnet publish` plus the built SPA, served by a **systemd**-managed Kestrel
behind **nginx**, **HTTPS-only** (port 80 serves only the ACME challenge and
redirects to HTTPS). The committed `nginx.conf` owns the TLS config;
`certbot certonly --webroot` only issues the cert. There is no database, so
there is **no migration step**. Non-secret prod config ships in
`appsettings.Production.json`; the GitHub token and the bot API key live in
`/etc/snipcache/secrets.env` on the server.

```
scripts/deploy/
  deploy.ps1                 PowerShell orchestrator (build -> scp -> invoke bash)
  lib/                       reusable, app-agnostic server-side bash (source deploy.env)
    setup.sh                 first-time: user, dirs, perms, secrets template, nginx + unit, DuckDNS timer
    install.sh               routine: converge nginx -> stop -> rsync server/client -> restart -> healthcheck
    config.sh                config-only: reinstall nginx + unit, reload (no build)
  ddns/                      DuckDNS updater (installed by -Setup when DuckDnsDomain is set)
    duckdns-update.sh        -> /usr/local/bin/duckdns-update
    duckdns@.service/.timer  -> /etc/systemd/system/ (instance = subdomain, every 5 min)
  apps/
    snipcache.ps1            per-app VARIABLES (host, domain, port, build paths, DuckDnsDomain)
    snipcache/               per-app server assets
      nginx.conf             -> /etc/nginx/sites-available/snipcache
      snipcache.service      -> /etc/systemd/system/snipcache.service
      secrets.env.example    -> /etc/snipcache/secrets.env (once, never overwritten)
      appsettings.Production.json   non-secret prod config (overlaid onto the publish)
```

**How it works.** `deploy.ps1 <app>` dot-sources `apps/<app>.ps1` for its variables,
builds locally, writes a `deploy.env` from those variables, `scp`s the artifacts +
per-app assets + `deploy.env` to `~/<app>-upload/`, then streams the matching
`lib/*.sh` over SSH. Each bash script `source`s `deploy.env`, so it is fully
app-agnostic — the only app-specific things on the server are the uploaded files.

Requirements on the deploying machine: PowerShell 7 (`pwsh`), `ssh`/`scp` with a
host alias `tank` and passwordless `sudo` for that user on the server, .NET 10
SDK, pnpm.

## Layout on the server

| Path                               | Purpose                                     | Owner            |
| ---------------------------------- | ------------------------------------------- | ---------------- |
| `/opt/snipcache`                   | published backend (`SnipCacheEditor.dll`)   | `snipcache`      |
| `/var/www/snipcache`               | built SPA (Vite `dist`)                     | `www-data`       |
| `/var/lib/snipcache/data/dp-keys`  | Data Protection keys (logins persist)       | `snipcache`      |
| `/var/lib/snipcache/data/logs`     | Serilog rolling files (compact JSON)        | `snipcache`      |
| `/etc/snipcache/secrets.env`       | `GitHub__Token`, `Bot__ApiKey` (mode 600)   | root:`snipcache` |
| `/etc/duckdns/snipcache.token`     | DuckDNS token (mode 600)                    | root             |

Kestrel listens on `127.0.0.1:5004` (showcase=5003, Blondi=5002, verby=5001);
nginx terminates TLS and proxies `/api/`, `/auth/` and `/bot/` to it. `/health`
is not proxied: `ssh tank curl -s http://127.0.0.1:5004/health`.

## Dynamic DNS

`snipcache.duckdns.org` is updated by `duckdns@snipcache.timer` (every 5 minutes,
and 1 minute after boot) through the [DuckDNS API](https://www.duckdns.org/spec.jsp):

- **AAAA**: the host's stable global IPv6 address on the default-route interface
  (temporary/privacy and ULA addresses are skipped), sent explicitly, because the
  ISP prefix can change.
- **A**: sent empty over IPv4, so DuckDNS stores the source address of the
  request — the router's public IPv4. The router must forward 80/443 to `tank`.

The token is never in the repo. It sits in `/etc/duckdns/snipcache.token`
(root, 600) and reaches the unprivileged updater as a systemd credential, piped
to `curl` on stdin (not on the command line). Check it with:

```sh
ssh tank "systemctl list-timers 'duckdns@*'; journalctl -u duckdns@snipcache -n 5 --no-pager"
```

## First-time setup

1. **Keycloak** (`cestius` realm on `auth-colmena.freeddns.org`), client `snipcache`:
   public, Standard flow, PKCE S256, client authentication off.
   - Valid redirect URIs: `https://snipcache.duckdns.org/auth/*`, `https://localhost:5173/auth/*`
   - Valid post-logout redirect URIs: `https://snipcache.duckdns.org/*`, `https://localhost:5173/*`
   - Web origins: `https://snipcache.duckdns.org`, `https://localhost:5173`
   - Client role `snipcache-editor`, assigned to your user, plus a mapper
     "User Client Role" (client `snipcache`) with claim name `roles`, multivalued,
     **Add to ID token** on (the BFF reads roles from the ID token).
2. **Provision + first deploy:**
   ```powershell
   ./scripts/deploy/deploy.ps1 snipcache -Setup
   ```
   Creates the service user and directories, `/etc/snipcache/secrets.env` from
   the template, the nginx site (HTTP-only ACME bootstrap), the systemd unit and
   the DuckDNS units, then installs backend + SPA. The service fails to start
   until the secrets are filled in; that is expected on the first run.
3. **Secrets and DuckDNS token** (on the server):
   ```sh
   ssh -t tank sudoedit /etc/snipcache/secrets.env      # GitHub__Token, Bot__ApiKey
   ssh -t tank sudoedit /etc/duckdns/snipcache.token    # the DuckDNS token, one line
   ./scripts/deploy/deploy.ps1 snipcache -Setup         # enables the DuckDNS timer, restarts
   ```
4. **Enable HTTPS** (after `snipcache.duckdns.org` resolves to tank). The
   committed `nginx.conf` is HTTPS-only and owns the TLS config; certbot only
   issues the cert:
   ```powershell
   ssh tank "sudo certbot certonly --webroot -w /var/www/certbot -d snipcache.duckdns.org"
   ./scripts/deploy/deploy.ps1 snipcache -Setup
   ```
   `certonly` writes only to `/etc/letsencrypt` (never nginx config); the re-run of
   `-Setup` swaps the temporary HTTP bootstrap for the committed HTTPS config and
   reloads. Renewals reload nginx automatically via the deploy hook setup installs.

## Routine deploys

```powershell
./scripts/deploy/deploy.ps1 snipcache             # client + server
./scripts/deploy/deploy.ps1 snipcache -ServerOnly
./scripts/deploy/deploy.ps1 snipcache -ClientOnly
./scripts/deploy/deploy.ps1 snipcache -ConfigOnly # re-push nginx + unit, reload
```

There's also a root-level `pnpm` shortcut (just forwards to the script above):

```sh
pnpm deploy:snipcache                # client + server
pnpm deploy:snipcache -- -ServerOnly # forward flags after `--`
```

Each server deploy re-applies the committed `nginx.conf` (validated with `nginx -t`
before the service is touched, and skipped on a cert-less box where the HTTP
bootstrap must stay), stops the service, rsyncs the new binaries, refreshes the
unit, then restarts, reloads nginx, and health-checks. Secret rotation (PAT, bot
key): edit `/etc/snipcache/secrets.env`, then `ssh tank sudo systemctl restart snipcache`.

## Onboarding another app

Copy `apps/snipcache.ps1` to `apps/<app>.ps1` and adjust the variables, then create
`apps/<app>/` with its own `nginx.conf`, `<app>.service`,
`appsettings.Production.json` and, if it has secrets, `secrets.env.example`.
`deploy.ps1` and everything in `lib/` stay untouched. Give the new app a free
loopback port and its own Keycloak client URIs.
