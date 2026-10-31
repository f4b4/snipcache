# Per-app deploy configuration for the Showcase app.
#
# This file holds VARIABLES ONLY. The generic orchestrator (../deploy.ps1)
# dot-sources it, expects the hashtable below as its last expression, and does
# all the work. To onboard another app, copy this file (e.g. blondi.ps1) and
# change the values — deploy.ps1 and lib/*.sh stay untouched.
#
# AppName drives every server-side name: the systemd service, the service user,
# /opt/<AppName>, /var/www/<AppName>, /var/lib/<AppName>, and the per-app assets
# folder apps/<AppName>/ (nginx.conf, <AppName>.service, appsettings.Production.json).

@{
    # --- Target ---
    AppName      = 'showcase'                  # service / user / paths / assets-folder name
    SshHost      = 'tank'                      # ssh alias of the VPS (shared with Blondi)
    Domain       = 'showcase.freeddns.org'     # public host: nginx server_name + TLS cert
    Port         = 5003                         # Kestrel loopback port (Blondi=5002, verby=5001)

    # --- Local build inputs (relative to the repo root) ---
    ClientDir    = 'frontend/apps/showcase'                    # SPA project; build emits ./dist here
    ClientFilter = 'showcase'                                  # pnpm --filter <name> for the build
    ServerProj   = 'backend/apps/Showcase/Showcase.csproj'     # .NET project to publish
    ServerDll    = 'Showcase.dll'                              # entrypoint assembly in the publish output
}
