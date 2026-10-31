#!/usr/bin/env bash
# Routine deploy: install the freshly-uploaded backend and/or SPA, converge the
# nginx site + systemd unit, and restart. App-agnostic — all values come from
# deploy.env. No database, so no migration step.
#
# Invoked by deploy.ps1 as:  bash -s -- <UPLOAD_DIR>   (streamed over SSH stdin)
set -euo pipefail

UPLOAD_DIR="${1:?upload dir arg missing}"
# shellcheck source=/dev/null
source "$UPLOAD_DIR/deploy.env"

: "${APP_NAME:?}" "${SERVICE_USER:?}" "${DOMAIN:?}"
DEPLOY_SERVER="${DEPLOY_SERVER:-0}"
DEPLOY_CLIENT="${DEPLOY_CLIENT:-0}"

# Converge the committed nginx.conf on every deploy so an edit to it actually
# lands without needing a separate -ConfigOnly run (it's HTTPS-only and owns TLS,
# so re-applying never reverts HTTPS). Do this BEFORE touching the running service
# and validate with `nginx -t`, so a bad config aborts the deploy with the app
# still up. Guard on the cert: on a fresh box the cert doesn't exist yet and the
# HTTPS config would fail `nginx -t`, so leave setup.sh's HTTP bootstrap in place
# until the cert is issued and -Setup converges to HTTPS. The reload at the end
# applies it.
echo "--- nginx config ---"
# Probe with sudo: certbot keeps /etc/letsencrypt/{live,archive} at 0700 root, so a
# bare unprivileged `[ -e ]` can't traverse into live/ and falsely reports "no cert".
CERT="/etc/letsencrypt/live/$DOMAIN/fullchain.pem"
if sudo test -e "$CERT"; then
    sudo cp "$UPLOAD_DIR/config/nginx.conf" "/etc/nginx/sites-available/$APP_NAME"
    sudo nginx -t
else
    echo "No TLS cert at $CERT yet — keeping the HTTP bootstrap (issue the cert, then run -Setup)."
fi

# Stop before swapping binaries (a no-op on the very first deploy).
if systemctl is-active --quiet "$APP_NAME" 2>/dev/null; then
    echo "--- Stopping $APP_NAME ---"
    sudo systemctl stop "$APP_NAME"
fi

if [ "$DEPLOY_SERVER" = "1" ]; then
    echo "--- Installing backend -> /opt/$APP_NAME ---"
    sudo rsync -a --delete "$UPLOAD_DIR/server/" "/opt/$APP_NAME/"
    sudo chown -R "$SERVICE_USER:$SERVICE_USER" "/opt/$APP_NAME"
fi

if [ "$DEPLOY_CLIENT" = "1" ]; then
    echo "--- Installing frontend -> /var/www/$APP_NAME ---"
    sudo rsync -a --delete "$UPLOAD_DIR/dist/" "/var/www/$APP_NAME/"
    sudo chown -R www-data:www-data "/var/www/$APP_NAME"
fi

echo "--- Refreshing systemd unit ---"
sudo cp "$UPLOAD_DIR/config/$APP_NAME.service" "/etc/systemd/system/$APP_NAME.service"
sudo systemctl daemon-reload

echo "--- Starting services ---"
sudo systemctl start "$APP_NAME"
sudo systemctl reload nginx

echo "--- Health check ---"
sleep 2
if systemctl is-active --quiet "$APP_NAME"; then
    echo "$APP_NAME: running"
else
    echo "$APP_NAME: FAILED — inspect with: ssh <host> journalctl -u $APP_NAME -n 50"
    exit 1
fi
