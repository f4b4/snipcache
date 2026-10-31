#!/usr/bin/env bash
# First-time provisioning for a Cestius app. App-agnostic: every value comes from
# the deploy.env that deploy.ps1 generated and uploaded. Idempotent — safe to re-run.
#
# Invoked by deploy.ps1 as:  bash -s -- <UPLOAD_DIR>   (streamed over SSH stdin)
set -euo pipefail

UPLOAD_DIR="${1:?upload dir arg missing}"
# shellcheck source=/dev/null
source "$UPLOAD_DIR/deploy.env"

: "${APP_NAME:?}" "${DOMAIN:?}" "${SERVICE_USER:?}"

echo "--- Service account ($SERVICE_USER) ---"
if ! id "$SERVICE_USER" &>/dev/null; then
    sudo useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
    echo "created."
else
    echo "already exists."
fi

echo "--- Directories ---"
sudo mkdir -p "/opt/$APP_NAME"                      # published backend
sudo mkdir -p "/var/www/$APP_NAME"                  # built SPA
sudo mkdir -p "/var/lib/$APP_NAME/data/dp-keys"     # DataProtection key ring (logins survive restarts)
sudo mkdir -p "/var/lib/$APP_NAME/data/logs"
sudo mkdir -p /var/www/certbot                      # ACME challenge web root

sudo chown -R "$SERVICE_USER:$SERVICE_USER" "/opt/$APP_NAME" "/var/lib/$APP_NAME"
sudo chmod 700 "/var/lib/$APP_NAME/data/dp-keys"
sudo chown -R www-data:www-data "/var/www/$APP_NAME"

echo "--- nginx site ---"
# Chicken-and-egg: the committed nginx.conf is HTTPS-only and references the
# Let's Encrypt cert, so `nginx -t` fails before the cert exists. Until it does,
# install a tiny HTTP-only ACME stub so nginx runs and certbot can validate over
# port 80; once the cert is present, converge to the committed TLS config.
# Re-running -Setup after issuing the cert is what flips the site to HTTPS.
# Probe with sudo: certbot keeps /etc/letsencrypt/{live,archive} at 0700 root, so a
# bare unprivileged `[ -e ]` can't traverse into live/ and falsely reports "no cert"
# (which would re-install the HTTP bootstrap over a working HTTPS site on re-run).
CERT="/etc/letsencrypt/live/$DOMAIN/fullchain.pem"
if sudo test -e "$CERT"; then
    echo "cert present -> installing committed HTTPS config."
    sudo cp "$UPLOAD_DIR/config/nginx.conf" "/etc/nginx/sites-available/$APP_NAME"
else
    echo "no cert yet -> installing temporary HTTP-only ACME bootstrap."
    sudo tee "/etc/nginx/sites-available/$APP_NAME" >/dev/null <<EOF
# TEMPORARY ACME bootstrap for $APP_NAME — replaced by the committed HTTPS
# nginx.conf on the next -Setup once the cert exists. Do not edit by hand.
server {
    listen 80;
    server_name $DOMAIN;
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 503; }
}
EOF
fi
sudo ln -sf "/etc/nginx/sites-available/$APP_NAME" "/etc/nginx/sites-enabled/$APP_NAME"
sudo nginx -t
sudo systemctl enable nginx
sudo systemctl reload nginx

echo "--- certbot renewal hook ---"
# webroot renewals don't reload nginx on their own (the --nginx plugin used to).
# A global deploy hook reloads nginx whenever any cert is renewed. Idempotent.
sudo mkdir -p /etc/letsencrypt/renewal-hooks/deploy
sudo tee /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh >/dev/null <<'EOF'
#!/bin/sh
systemctl reload nginx
EOF
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh

echo "--- systemd unit ---"
sudo cp "$UPLOAD_DIR/config/$APP_NAME.service" "/etc/systemd/system/$APP_NAME.service"
sudo systemctl daemon-reload
sudo systemctl enable "$APP_NAME"

cat <<EOF

=======================================================
Setup complete for '$APP_NAME'.

NEXT STEPS (once only):
  1. DNS: point $DOMAIN at this host.

  2. Keycloak: in the 'cestius' realm, add to the '$APP_NAME' client:
       - Valid redirect URI:  https://$DOMAIN/*
       - Web origin:          https://$DOMAIN
       - Valid post-logout:   https://$DOMAIN/*

  3. Enable HTTPS (after DNS resolves here):
       a. Issue the cert (certbot only writes to /etc/letsencrypt, never nginx.conf):
            sudo certbot certonly --webroot -w /var/www/certbot -d $DOMAIN
       b. Re-run setup to swap the HTTP bootstrap for the committed HTTPS config:
            ./scripts/deploy/deploy.ps1 $APP_NAME -Setup
     TLS lives in the committed nginx.conf, so there is nothing to copy back and
     -ConfigOnly never reverts it. Renewals reload nginx via a deploy hook.

The app binaries/SPA are installed by the same deploy run (install.sh) right after
this. If you ran -Setup alone, deploy now:  ./scripts/deploy/deploy.ps1 $APP_NAME
=======================================================
EOF
