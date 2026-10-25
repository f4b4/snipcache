import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, apiFetch, redirectToLogin, UnauthenticatedError } from "./client";
import { configureAuth, DEFAULT_AUTH_ENDPOINTS } from "./config";

/**
 * The BFF fetch shim. We mock global fetch and inspect the request it builds,
 * and stub window.location so the 401 / redirect path is observable in jsdom
 * (which otherwise refuses real navigation).
 */

const realLocation = window.location;

function fakeResponse(status: number): Response {
  // apiFetch only reads `.status`, so a minimal stand-in is enough.
  return { status } as Response;
}

beforeEach(() => {
  Object.defineProperty(window, "location", {
    configurable: true,
    value: { href: "", pathname: "/dash", search: "?x=1" } as Location,
  });
});

afterEach(() => {
  Object.defineProperty(window, "location", {
    configurable: true,
    value: realLocation,
  });
  configureAuth(DEFAULT_AUTH_ENDPOINTS); // reset the endpoint singleton
  vi.unstubAllGlobals();
});

describe("apiFetch", () => {
  it("does not add X-CSRF on a GET, but includes credentials", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse(200));
    vi.stubGlobal("fetch", fetchMock);

    await apiFetch("/api/things");

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.has("X-CSRF")).toBe(false);
    expect(init.credentials).toBe("include");
  });

  it("adds X-CSRF: '1' on a mutating verb", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse(200));
    vi.stubGlobal("fetch", fetchMock);

    await apiFetch("/api/things", { method: "POST" });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get("X-CSRF")).toBe("1");
    expect(init.credentials).toBe("include");
  });

  it("redirects to login and throws UnauthenticatedError on 401", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse(401));
    vi.stubGlobal("fetch", fetchMock);

    await expect(apiFetch("/api/secure")).rejects.toBeInstanceOf(
      UnauthenticatedError,
    );
    expect(window.location.href).toBe(
      `/auth/login?returnUrl=${encodeURIComponent("/dash?x=1")}`,
    );
  });
});

describe("redirectToLogin", () => {
  it("honors a configureAuth() endpoint override", () => {
    configureAuth({ login: "/bff/login" });
    redirectToLogin("/back");
    expect(window.location.href).toBe(
      `/bff/login?returnUrl=${encodeURIComponent("/back")}`,
    );
  });
});

// api.* are the JSON convenience wrappers over apiFetch: they parse the body,
// treat 204 as "no content", and surface a non-2xx as a thrown Error.
function jsonResponse(status: number, body: unknown): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
  } as unknown as Response;
}

describe("api.get", () => {
  it("returns the parsed JSON body on 200", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse(200, { id: 7 })),
    );

    await expect(api.get<{ id: number }>("/api/thing")).resolves.toEqual({
      id: 7,
    });
  });

  it("returns undefined on 204 without reading the body", async () => {
    const json = vi.fn();
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ status: 204, ok: true, json } as unknown as Response),
    );

    await expect(api.get("/api/thing")).resolves.toBeUndefined();
    expect(json).not.toHaveBeenCalled();
  });

  it("throws on a non-ok response", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(500, {})));

    await expect(api.get("/api/thing")).rejects.toThrow("GET /api/thing → 500");
  });
});

describe("api.send", () => {
  it("serializes the body, sets JSON + X-CSRF headers, and returns the result", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { ok: true }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(api.send("POST", "/api/thing", { name: "x" })).resolves.toEqual({
      ok: true,
    });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get("Content-Type")).toBe("application/json");
    expect(headers.get("X-CSRF")).toBe("1");
    expect(init.body).toBe(JSON.stringify({ name: "x" }));
  });

  it("omits the request body when none is given", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(204, undefined));
    vi.stubGlobal("fetch", fetchMock);

    await expect(api.send("DELETE", "/api/thing")).resolves.toBeUndefined();

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(init.body).toBeUndefined();
  });

  it("throws on a non-ok response", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(400, {})));

    await expect(api.send("POST", "/api/thing", {})).rejects.toThrow(
      "POST /api/thing → 400",
    );
  });
});
