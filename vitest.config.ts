import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// One runner for every package, split into two projects: the site builder and
// the shared content rules run under Node, the React packages under jsdom.
// Workspace packages export their TypeScript source, so no aliases are needed.
export default defineConfig({
  plugins: [react()],
  test: {
    // No need to import describe/it/expect/vi in every file.
    globals: true,
    // Skip build artifacts, dependencies and generated site content.
    exclude: [
      "**/node_modules/**",
      "**/dist/**",
      "**/.{git,cache,astro}/**",
      "site/src/content/docs/**",
      "site/public/raw/**",
    ],
    projects: [
      {
        extends: true,
        test: {
          name: "node",
          environment: "node",
          include: [
            "site/**/*.{test,spec}.ts",
            "frontend/shared/content-rules/**/*.{test,spec}.ts",
          ],
        },
      },
      {
        extends: true,
        test: {
          name: "dom",
          environment: "jsdom",
          // jest-dom matchers + matchMedia polyfill (see frontend/test/setup.ts).
          setupFiles: ["./frontend/test/setup.ts"],
          include: ["frontend/**/*.{test,spec}.{ts,tsx}"],
          // Added to the shared excludes above.
          exclude: ["frontend/shared/content-rules/**"],
        },
      },
    ],
  },
});
