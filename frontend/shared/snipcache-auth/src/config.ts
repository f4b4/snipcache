/**
 * Endpoint configuration for the BFF auth client.
 *
 * apiFetch()/redirectToLogin() run *outside* React (Orval's generated fetcher
 * calls them), so the endpoints can't come from context — they live in this
 * module-level singleton. Apps override once at startup via configureAuth(),
 * and every read goes through getAuthEndpoints() so the override always wins.
 */

export interface AuthEndpoints {
  /** Full-page redirect target that starts the Keycloak login flow. */
  login: string;
  /** POST target that clears the server session; returns `{ logoutUrl }`. */
  logout: string;
  /** The "who am I" probe the provider calls on startup. */
  me: string;
}

/** SnipCache BFF defaults. Apps proxying under a different base override these. */
export const DEFAULT_AUTH_ENDPOINTS: AuthEndpoints = {
  login: "/auth/login",
  logout: "/auth/logout",
  me: "/auth/me",
};

const current: AuthEndpoints = { ...DEFAULT_AUTH_ENDPOINTS };

/**
 * Overrides one or more endpoint paths. Call once at startup (before the
 * provider mounts) so the bootstrap probe and the module-level client agree.
 */
export function configureAuth(overrides: Partial<AuthEndpoints>): void {
  Object.assign(current, overrides);
}

/** The current endpoints. Read at call-time so overrides are picked up. */
export function getAuthEndpoints(): Readonly<AuthEndpoints> {
  return current;
}
