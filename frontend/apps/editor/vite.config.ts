import fs from "node:fs";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// The SnipCacheEditor backend (dotnet run --launch-profile https).
const backend = "https://localhost:7240";

// Dev runs over HTTPS so the BFF auth cookie (Secure) and the OIDC redirect_uri
// (https://localhost:5173/auth/signin-oidc) work. Certs are produced by mkcert
// (run once, from this directory):
//   mkcert -install            # trust the local CA (one-time, machine-wide)
//   mkcert localhost           # writes localhost.pem / localhost-key.pem here
// They're gitignored (*.pem). The https block is only applied when the certs are
// present, so `vite build` still works in CI / on a fresh checkout without them.
// Without certs the dev server runs on http://localhost:5173, which is enough for
// the fake repository and dev login (browsers accept Secure cookies on localhost).
const keyPath = fileURLToPath(new URL("./localhost-key.pem", import.meta.url));
const certPath = fileURLToPath(new URL("./localhost.pem", import.meta.url));
const httpsCerts =
  fs.existsSync(keyPath) && fs.existsSync(certPath)
    ? { key: fs.readFileSync(keyPath), cert: fs.readFileSync(certPath) }
    : undefined;

// The backend runs the OIDC flow and owns the session cookie; /api, /auth and
// /bot are proxied to it so the browser stays same-origin. secure:false accepts
// the dev backend certificate.
const proxied = { target: backend, secure: false, changeOrigin: true };

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    // Monaco is one ~2.7 MB chunk, loaded lazily when the first file opens.
    chunkSizeWarningLimit: 3000,
  },
  server: {
    // Spread so the key is simply absent (HTTP) when certs aren't generated yet.
    ...(httpsCerts ? { https: httpsCerts } : {}),
    host: "localhost",
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": proxied,
      "/auth": proxied,
      "/bot": proxied,
    },
  },
});
