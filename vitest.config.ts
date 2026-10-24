import { defineConfig } from "vitest/config";

// One runner for every frontend package. The React plugin, jsdom and the
// "@snipcache/auth" alias are added with the editor frontend (phase 4).
export default defineConfig({
  test: {
    // No need to import describe/it/expect/vi in every file.
    globals: true,
    // Discover *.test.ts(x) / *.spec.ts(x) anywhere in the workspace, but skip
    // build artifacts, dependencies and generated site content.
    include: ["**/*.{test,spec}.{ts,tsx}"],
    exclude: [
      "**/node_modules/**",
      "**/dist/**",
      "**/.{git,cache,astro}/**",
      "site/src/content/docs/**",
      "site/public/raw/**",
    ],
  },
});
