/**
 * Global setup for the jsdom test project, loaded once per worker (see
 * vitest.config.ts → setupFiles).
 *
 * 1. Registers @testing-library/jest-dom matchers (toBeInTheDocument, etc.).
 * 2. Polyfills window.matchMedia, which jsdom does not implement. The editor
 *    reads `prefers-color-scheme` to pick the Monaco theme.
 * 3. Cleans up the DOM and unstubs globals between tests.
 */
import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, beforeEach, vi } from "vitest";

// Defaults to "light" (matches: false). Individual tests can override via
// vi.spyOn(window, "matchMedia") if they need dark-mode behavior.
function createMatchMedia(matches: boolean) {
  return (query: string): MediaQueryList =>
    ({
      matches,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(), // deprecated, kept for older callers
      removeListener: vi.fn(), // deprecated
      dispatchEvent: vi.fn(),
    }) as unknown as MediaQueryList;
}

beforeEach(() => {
  vi.stubGlobal("matchMedia", createMatchMedia(false));
});

afterEach(() => {
  cleanup();
  window.localStorage.clear();
  vi.unstubAllGlobals();
});
