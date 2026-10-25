import { afterEach, describe, expect, it } from "vitest";
import {
  configureAuth,
  DEFAULT_AUTH_ENDPOINTS,
  getAuthEndpoints,
} from "./config";

/**
 * The endpoint singleton lives outside React so the module-level client and the
 * provider's bootstrap probe agree. These tests pin its contract: defaults,
 * partial override, and read-at-call-time (so a later override always wins).
 */
afterEach(() => {
  configureAuth(DEFAULT_AUTH_ENDPOINTS); // reset the shared singleton
});

describe("auth endpoint config", () => {
  it("starts from the SnipCache BFF defaults", () => {
    expect(getAuthEndpoints()).toEqual({
      login: "/auth/login",
      logout: "/auth/logout",
      me: "/auth/me",
    });
  });

  it("merges a partial override, leaving the rest at their defaults", () => {
    configureAuth({ me: "/bff/me" });

    expect(getAuthEndpoints()).toEqual({
      login: "/auth/login",
      logout: "/auth/logout",
      me: "/bff/me",
    });
  });

  it("reads at call time, so a later override wins", () => {
    const before = getAuthEndpoints().login;
    configureAuth({ login: "/bff/login" });

    expect(before).toBe("/auth/login");
    expect(getAuthEndpoints().login).toBe("/bff/login");
  });
});
