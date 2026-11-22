Fix this repo's nginx config so it listens on IPv6 as well as IPv4.

Background
- This app is deployed to the server `tank` (ssh alias), behind nginx. Several
  apps share that nginx: verby (colmena.freeddns.org + auth-colmena.freeddns.org
  for Keycloak), showcase.freeddns.org, blondi.freeddns.org, snipcache.duckdns.org.
- Every one of these host names has an AAAA record pointing at tank
  (2a02:3100:5859:d400:4ecc:6aff:fe63:a08e) as well as an A record.
- Only the snipcache site listened on [::]:80 / [::]:443. nginx can only pick
  among server blocks that listen on the socket a connection arrives on, so
  every IPv6 request (whatever host name it asked for) got the snipcache
  site's certificate. That broke Keycloak and the other apps over IPv6.
- I already fixed this by hand on tank: next to every IPv4 `listen` line in
  /etc/nginx/sites-available/{verby,showcase,blondi} I added the IPv6 one
  (`listen [::]:80;`, `listen [::]:443 ssl;`, or `listen [::]:443 ssl http2;`
  where the IPv4 line has http2). Backup of the originals:
  /etc/nginx/backup-ipv6-20261122-205041/.
- Problem: this repo's deploy scripts may reinstall the nginx config and wipe
  that fix. The repo has to carry it too.

What to do
1. Find every nginx config this repo installs or generates: committed
   nginx.conf / site files, and nginx server blocks written by the deploy
   scripts (e.g. an HTTP-only ACME "bootstrap" stub in a setup script, or
   heredocs in bash/PowerShell).
2. In every server block, put the IPv6 counterpart right after each IPv4
   listen line, with the same options:
     listen 80;                 -> + listen [::]:80;
     listen 443 ssl;            -> + listen [::]:443 ssl;
     listen 443 ssl http2;      -> + listen [::]:443 ssl http2;
   Leave everything else alone (certbot "managed by Certbot" comments, TLS
   settings, headers). Don't add default_server or ipv6only.
3. If the committed config is HTTP-only and certbot adds the 443 blocks on the
   server (`certbot --nginx`), still add `listen [::]:80;`. Then tell me
   whether a redeploy would overwrite the server's 443 blocks, and how the
   IPv6 443 listener should be kept (certbot keeps extra listen lines on
   renewal but won't add them).
4. Compare the result with the live file on tank (read-only:
   `ssh tank cat /etc/nginx/sites-available/<site>`). The repo version should
   produce the same listen lines. Report any other differences, but don't
   "fix" them.
5. If this repo also has a dynamic-DNS updater, check one thing: DuckDNS only
   auto-detects IPv4 when the request has no ipv6= parameter, so A and AAAA
   updates must be separate requests. Other providers may behave the same
   way. Report what you find; don't change it without asking.
6. Show me the diff. Don't deploy, don't touch tank, and don't commit until I
   say so.

How to check after a later deploy (I'll run it):
  ssh tank "sudo nginx -t"
  openssl s_client -6 -connect <host>:443 -servername <host> </dev/null 2>/dev/null | openssl x509 -noout -subject
The subject must be that host's own certificate, not snipcache.duckdns.org.