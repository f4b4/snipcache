#!/usr/bin/env bash
# Config-only push: reinstall nginx.conf + the systemd unit and reload, without
# building or touching the app binaries. App-agnostic — values from deploy.env.
#
# Invoked by deploy.ps1 as:  bash -s -- <UPLOAD_DIR>   (streamed over SSH stdin)
#
# The committed nginx.conf owns TLS (it references the Let's Encrypt cert paths
# directly), so re-applying it here is safe and never reverts HTTPS. It does
# require the cert to already exist — we guard on that below so we never install
# an HTTPS config that fails `nginx -t` on a box that hasn't been set up yet.
set -euo pipefail

UPLOAD_DIR="${1:?upload dir arg missing}"
# shellcheck source=/dev/null
source "$UPLOAD_DIR/deploy.env"

: "${APP_NAME:?}" "${DOMAIN:?}"

echo "--- nginx ---"
# Probe with sudo: certbot keeps /etc/letsencrypt/{live,archive} at 0700 root, so a
# bare unprivileged `[ -e ]` can't traverse into live/ and falsely reports "no cert".
CERT="/etc/letsencrypt/live/$DOMAIN/fullchain.pem"
if ! sudo test -e "$CERT"; then
    echo "No TLS cert at $CERT — the committed nginx.conf is HTTPS-only and would"
    echo "fail 'nginx -t'. Run first-time setup and issue the cert, then retry:"
    echo "  ./scripts/deploy/deploy.ps1 $APP_NAME -Setup"
    echo "  sudo certbot certonly --webroot -w /var/www/certbot -d $DOMAIN"
    echo "  ./scripts/deploy/deploy.ps1 $APP_NAME -Setup"
    exit 1
fi
sudo cp "$UPLOAD_DIR/config/nginx.conf" "/etc/nginx/sites-available/$APP_NAME"
sudo nginx -t
sudo systemctl reload nginx

echo "--- systemd unit ---"
sudo cp "$UPLOAD_DIR/config/$APP_NAME.service" "/etc/systemd/system/$APP_NAME.service"
sudo systemctl daemon-reload
sudo systemctl restart "$APP_NAME"

echo "Config updated for '$APP_NAME'."
