# Per-app deploy configuration for the SnipCache editor.
#
# This file holds VARIABLES ONLY. The generic orchestrator (../deploy.ps1)
# dot-sources it, expects the hashtable below as its last expression, and does
# all the work. To onboard another app, copy this file and change the values —
# deploy.ps1 and lib/*.sh stay untouched.
#
# AppName drives every server-side name: the systemd service, the service user,
# /opt/<AppName>, /var/www/<AppName>, /var/lib/<AppName>, /etc/<AppName>, and the
# per-app assets folder apps/<AppName>/ (nginx.conf, <AppName>.service,
# appsettings.Production.json, optional secrets.env.example).

@{
    # --- Target ---
    AppName      = 'snipcache'                 # service / user / paths / assets-folder name
    SshHost      = 'tank'                      # ssh alias of the server (shared with the Cestius apps)
    Domain       = 'snipcache.duckdns.org'     # public host: nginx server_name + TLS cert
    Port         = 5004                        # Kestrel loopback port (showcase=5003, Blondi=5002, verby=5001)

    # --- Local build inputs (relative to the repo root) ---
    ClientDir    = 'frontend/apps/editor'                                  # SPA project; build emits ./dist here
    ClientFilter = 'editor'                                                # pnpm --filter <name> for the build
    ServerProj   = 'backend/apps/SnipCacheEditor/SnipCacheEditor.csproj'   # .NET project to publish
    ServerDll    = 'SnipCacheEditor.dll'                                   # entrypoint assembly in the publish output

    # --- Optional ---
    DuckDnsDomain = 'snipcache'                # -Setup installs the DuckDNS updater timer for <name>.duckdns.org
}
