import { useSyncExternalStore } from "react";

const QUERY = "(prefers-color-scheme: dark)";

function subscribe(onChange: () => void): () => void {
  const media = window.matchMedia(QUERY);
  media.addEventListener("change", onChange);
  return () => media.removeEventListener("change", onChange);
}

/** The OS color scheme, updated live; the CSS tokens follow the same query. */
export function useColorScheme(): "light" | "dark" {
  return useSyncExternalStore(subscribe, () => (window.matchMedia(QUERY).matches ? "dark" : "light"));
}
