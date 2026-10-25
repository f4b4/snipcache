import { defineConfig } from "orval";

/**
 * Generates TanStack Query hooks from the backend's build-time OpenAPI document.
 *
 * Input is the committed spec emitted by `dotnet build` of SnipCacheEditor
 * (backend/apps/SnipCacheEditor/openapi/openapi.json). Every generated call is
 * routed through the `apiFetchMutator` custom client (src/api/mutator.ts), which
 * delegates to @snipcache/auth's `apiFetch`, so all backend traffic inherits the
 * BFF behavior (cookies, X-CSRF, 401 → login) and no component or generated file
 * ever constructs its own fetch.
 *
 * Run with `pnpm --filter editor generate:api` after the backend spec changes.
 * Generated files live under src/api/generated/ and are committed; CI verifies
 * they are in sync.
 */
export default defineConfig({
  editor: {
    input: {
      target: "../../../backend/apps/SnipCacheEditor/openapi/openapi.json",
    },
    output: {
      mode: "split",
      client: "react-query",
      httpClient: "fetch",
      target: "./src/api/generated/editor.ts",
      schemas: "./src/api/generated/model",
      clean: true,
      override: {
        mutator: {
          path: "./src/api/mutator.ts",
          name: "apiFetchMutator",
        },
        // Hooks resolve to the parsed body (T), not { status, data, headers }.
        fetch: {
          includeHttpResponseReturnType: false,
        },
      },
    },
  },
});
