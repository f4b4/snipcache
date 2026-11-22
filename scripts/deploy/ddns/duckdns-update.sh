#!/bin/sh
# DuckDNS updater: points <name>.duckdns.org at this host (AAAA + A).
#
# Installed by lib/setup.sh as /usr/local/bin/duckdns-update and run by
# duckdns@<name>.timer every 5 minutes. API: https://www.duckdns.org/spec.jsp
#
#   IPv6: the host's own global address (not a privacy/temporary one) on the
#         default-route interface, sent explicitly — the ISP prefix can change.
#   IPv4: omitted and sent over IPv4, so DuckDNS records the router's public
#         address from the request (port forwarding to this host is the router's job).
#         DuckDNS only auto-detects IPv4 when the request carries no ipv6=, so
#         the two records are updated in two separate requests.
#
# The token comes from systemd's credential store ($CREDENTIALS_DIRECTORY/token,
# loaded from /etc/duckdns/<name>.token) and is passed to curl on stdin, so it
# never shows up in the process list or the journal.
set -eu

name="${1:?usage: duckdns-update <subdomain>}"
if [ -n "${CREDENTIALS_DIRECTORY:-}" ]; then
    token_file="$CREDENTIALS_DIRECTORY/token"     # under systemd (LoadCredential=)
else
    token_file="/etc/duckdns/$name.token"         # manual run as root
fi
token="$(tr -d ' \t\r\n' < "$token_file")"
if [ -z "$token" ]; then
    echo "duckdns: no token in /etc/duckdns/$name.token — nothing to do." >&2
    exit 1
fi

dev="$(ip -6 route show default | awk '{ for (i = 1; i < NF; i++) if ($i == "dev") { print $(i + 1); exit } }')"
ipv6="$(ip -6 -o addr show ${dev:+dev "$dev"} scope global primary -deprecated -tentative \
    | awk '{ sub("/.*", "", $4); print $4 }' | grep -iv '^f[cd]' | head -n 1 || true)"

# verbose=true answers "OK\n<ipv4>\n<ipv6>\nUPDATED|NOCHANGE".
update() {
    response="$(printf 'url = "https://www.duckdns.org/update?domains=%s&token=%s%s&verbose=true"\n' \
        "$name" "$token" "$1" \
        | curl -4 --silent --show-error --fail --max-time 30 --config -)"
    case "$response" in
        OK*) echo "duckdns: $(echo "$response" | tr '\n' ' ')" ;;
        *) echo "duckdns: update failed: $response" >&2; return 1 ;;
    esac
}

update ""                                   # A: auto-detected from the request
[ -z "$ipv6" ] || update "&ipv6=$ipv6"      # AAAA: explicit
