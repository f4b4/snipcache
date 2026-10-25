/**
 * Central fetch wrapper for the cookie-session (BFF) backend.
 *
 * Every call carries the auth cookie (`credentials: "include"`) and, for
 * mutating verbs, the `X-CSRF` header the server's CSRF guard requires. A 401
 * means the session is gone — we bounce to the Keycloak login flow.
 *
 * This module is the BFF client contract: it depends only on the `/auth/*`
 * endpoints (see config.ts) and the X-CSRF convention. Orval's generated client
 * is wired to call apiFetch as its fetcher, so it lives outside React.
 */
import { getAuthEndpoints } from "./config";

export class UnauthenticatedError extends Error {
  constructor() {
    super("Unauthenticated");
    this.name = "UnauthenticatedError";
  }
}

export function redirectToLogin(returnUrl?: string): void {
  const dest = returnUrl ?? window.location.pathname + window.location.search;
  const { login } = getAuthEndpoints();
  window.location.href = `${login}?returnUrl=${encodeURIComponent(dest)}`;
}

// Thin fetch shim: returns the raw Response so callers keep their own
// status/parse/retry logic, but with cookies + X-CSRF applied and a 401
// funneled into the login redirect.
export async function apiFetch(
  input: string,
  init: RequestInit = {},
): Promise<Response> {
  const method = (init.method ?? "GET").toUpperCase();
  const isMutating = method !== "GET" && method !== "HEAD";

  const headers = new Headers(init.headers);
  if (isMutating && !headers.has("X-CSRF")) headers.set("X-CSRF", "1");

  const res = await fetch(input, { ...init, credentials: "include", headers });

  if (res.status === 401) {
    redirectToLogin();
    throw new UnauthenticatedError();
  }
  return res;
}

// JSON convenience wrappers for the simple call sites.
export const api = {
  async get<T>(url: string): Promise<T> {
    const res = await apiFetch(url);
    if (!res.ok) throw new Error(`GET ${url} → ${res.status}`);
    return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
  },
  async send<T>(method: string, url: string, body?: unknown): Promise<T> {
    const init: RequestInit = {
      method,
      headers: { "Content-Type": "application/json" },
    };
    // Only set body when present — exactOptionalPropertyTypes forbids
    // assigning `undefined` to RequestInit.body.
    if (body !== undefined) init.body = JSON.stringify(body);
    const res = await apiFetch(url, init);
    if (!res.ok) throw new Error(`${method} ${url} → ${res.status}`);
    return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
  },
};
