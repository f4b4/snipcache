The Phase 6 files are written and the backend tests pass, but nothing is deployed yet: I can't reach `tank` from here. As of my last check there's still no SSH key in the agent and no `pwsh`. Nothing is committed.

**Deploy setup in [scripts/deploy/](scripts/deploy/)**
- **App config:** I removed the Cestius `showcase` config and added [apps/snipcache.ps1](scripts/deploy/apps/snipcache.ps1). The backend listens on local port 5004.
- **Production settings:** [appsettings.Production.json](scripts/deploy/apps/snipcache/appsettings.Production.json) uses the `cestius` realm and requires the `snipcache-editor` role, as you chose.
- **nginx:** [nginx.conf](scripts/deploy/apps/snipcache/nginx.conf) listens on IPv4 and IPv6 and proxies `/api`, `/auth` and `/bot`.
  - I fixed a bug copied from the Cestius config: `index.html` was served without the security headers (CSP, HSTS).
  - HSTS no longer has `preload`, because `duckdns.org` is a shared domain.
- **Secrets:** [secrets.env.example](scripts/deploy/apps/snipcache/secrets.env.example) is installed once as `/etc/snipcache/secrets.env` (mode 600) and never overwritten.
- **DuckDNS:** a timer in [ddns/](scripts/deploy/ddns/) runs every 5 minutes. It sends tank's stable IPv6 address for the AAAA record. DuckDNS takes the IPv4 for the A record from the request, which is the router's address. `-Setup` installs it.
- **DuckDNS token:** it is not in any repo file. It will only go into `/etc/duckdns/snipcache.token` on tank.
- **Generic scripts:** `deploy.ps1`, `setup.sh` and `install.sh` now handle the secrets template and the DuckDNS timer.

**Domain:** I replaced `snipcache.freeddns.org` with `snipcache.duckdns.org` in the code, the plan, the docs and the workflow description. The committer email changed with it. I also added a `pnpm deploy:snipcache` shortcut and updated CLAUDE.md and README.

**Bug fix:** once Keycloak is configured, an `/api` call without a session got a redirect to Keycloak instead of a 401. When a session expired, the SPA would show a network error instead of starting sign-in. I fixed it in the app's access rule ([EditorAuthorization.cs](backend/apps/SnipCacheEditor/EditorAuthorization.cs)), so the copied auth library stays identical to Cestius. [KeycloakChallengeTests.cs](backend/tests/SnipCacheEditor.Tests/KeycloakChallengeTests.cs) fails without the fix; with it all 174 tests pass. I also ran a local publish with the production settings: `/health` returns 200, sign-in redirects with `redirect_uri=https://snipcache.duckdns.org/auth/signin-oidc`, and `/bot` without a key returns 401.

**What you need to do so I can continue**
1. Install PowerShell 7 here, e.g. `sudo snap install powershell --classic`.
2. Give this machine SSH access to tank: `ssh-keygen -t ed25519`, then `ssh-copy-id chris@tank`. The name `tank` already resolves. Your `chris` user on tank needs passwordless `sudo`; the scripts run `sudo` over SSH without a terminal.
3. Create the Keycloak client `snipcache` in the `cestius` realm. The settings are in [scripts/deploy/README.md](scripts/deploy/README.md), step 1. The role mapper must add `roles` to the **ID token**.
4. Have the GitHub token and the bot key ready for `/etc/snipcache/secrets.env`.
5. Check that the router forwards ports 80 and 443 over IPv4 to tank. Otherwise the A record points at a router that doesn't pass traffic on.

Then I'll run the steps in order:
1. `-Setup`.
2. Write the DuckDNS token and the secrets file on tank.
3. `-Setup` again, which turns on the DuckDNS timer.
4. Issue the certificate, then `-Setup` once more to switch nginx to HTTPS.
5. Set `editor_url` in the content repo's workflow and push it, which redeploys the site with Edit links.
6. The end-to-end check.

Before the first `-Setup`, I'll look at the existing nginx sites on tank to make sure the IPv6 listen lines don't clash with them.