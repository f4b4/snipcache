// @ts-check
import { existsSync, readFileSync } from "node:fs";
import starlight from "@astrojs/starlight";
import { defineConfig } from "astro/config";

// All settings come from the environment so one build serves every content
// repo; the reusable workflow derives them from the repository name.
//   SNIPCACHE_SITE   origin, e.g. https://f4b4.github.io
//   SNIPCACHE_BASE   base path, e.g. /snipcache-content-f4b4 (default /)
//   SNIPCACHE_TITLE  site title (default SnipCache)
const env = process.env;

// Written by scripts/prepare-content.ts.
const sidebarFile = new URL("./src/generated/sidebar.json", import.meta.url);
const sidebar = existsSync(sidebarFile) ? JSON.parse(readFileSync(sidebarFile, "utf8")) : [];

export default defineConfig({
  site: env.SNIPCACHE_SITE || undefined,
  base: env.SNIPCACHE_BASE || "/",
  integrations: [
    starlight({
      title: env.SNIPCACHE_TITLE || "SnipCache",
      sidebar,
      components: {
        PageTitle: "./src/components/PageTitle.astro",
      },
      customCss: ["./src/styles/custom.css"],
    }),
  ],
});
