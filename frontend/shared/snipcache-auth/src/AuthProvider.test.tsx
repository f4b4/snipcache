import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AuthProvider } from "./AuthProvider";
import { useAuth } from "./useAuth";
import { configureAuth, DEFAULT_AUTH_ENDPOINTS } from "./config";

function Whoami() {
  const { user, isAuthenticated } = useAuth();
  return <span>{isAuthenticated ? `hi ${user?.name}` : "anon"}</span>;
}

function mockMe(response: Partial<Response> & { json?: () => Promise<unknown> }) {
  const fetchMock = vi.fn().mockResolvedValue(response);
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

afterEach(() => {
  configureAuth(DEFAULT_AUTH_ENDPOINTS); // reset the endpoint singleton
  vi.unstubAllGlobals();
});

describe("AuthProvider", () => {
  it("surfaces the user when /auth/me returns 200", async () => {
    mockMe({
      status: 200,
      ok: true,
      json: async () => ({
        sub: "u1",
        name: "Ada",
        email: "ada@example.com",
        roles: [],
        claims: {},
      }),
    });

    render(
      <AuthProvider>
        <Whoami />
      </AuthProvider>,
    );

    expect(await screen.findByText("hi Ada")).toBeInTheDocument();
  });

  it("treats 401 as unauthenticated (no service error)", async () => {
    mockMe({ status: 401, ok: false, json: async () => ({}) });

    render(
      <AuthProvider>
        <Whoami />
      </AuthProvider>,
    );

    expect(await screen.findByText("anon")).toBeInTheDocument();
    expect(screen.queryByText("Service unavailable")).not.toBeInTheDocument();
  });

  it("shows the default service-error UI when /auth/me returns 500", async () => {
    mockMe({ status: 500, ok: false, json: async () => ({}) });

    render(
      <AuthProvider>
        <Whoami />
      </AuthProvider>,
    );

    expect(await screen.findByText("Service unavailable")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
  });

  it("honors a custom renderServiceError", async () => {
    mockMe({ status: 503, ok: false, json: async () => ({}) });

    render(
      <AuthProvider renderServiceError={() => <p>custom boom</p>}>
        <Whoami />
      </AuthProvider>,
    );

    expect(await screen.findByText("custom boom")).toBeInTheDocument();
    expect(screen.queryByText("Service unavailable")).not.toBeInTheDocument();
  });

  it("applies the endpoints prop to the bootstrap probe", async () => {
    const fetchMock = mockMe({
      status: 200,
      ok: true,
      json: async () => null,
    });

    render(
      <AuthProvider endpoints={{ me: "/bff/me" }}>
        <Whoami />
      </AuthProvider>,
    );

    expect(await screen.findByText("anon")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith("/bff/me", {
      credentials: "include",
    });
  });
});

// login()/logout() drive a full-page navigation (window.location.href), so the
// session-action tests stub location to observe the destination. logout also
// branches on the POST result, so fetch is routed by method here.
function AuthControls() {
  const { isAuthenticated, user, login, logout } = useAuth();
  return (
    <div>
      <span>{isAuthenticated ? `hi ${user?.name}` : "anon"}</span>
      <button onClick={() => login("/dest")}>login</button>
      <button onClick={() => void logout()}>logout</button>
    </div>
  );
}

describe("AuthProvider session actions", () => {
  const realLocation = window.location;

  beforeEach(() => {
    Object.defineProperty(window, "location", {
      configurable: true,
      value: { href: "", pathname: "/dash", search: "" } as Location,
    });
  });

  afterEach(() => {
    Object.defineProperty(window, "location", {
      configurable: true,
      value: realLocation,
    });
  });

  // GET (the /auth/me bootstrap) → me handler; POST (/auth/logout) → logout handler.
  function mockRoutes(handlers: {
    me?: Partial<Response> & { json?: () => Promise<unknown> };
    logout?: () => Promise<Partial<Response> & { json?: () => Promise<unknown> }>;
  }) {
    const fetchMock = vi.fn((_input: string, init?: RequestInit) =>
      (init?.method ?? "GET").toUpperCase() === "POST"
        ? (handlers.logout?.() ?? Promise.resolve({} as Response))
        : Promise.resolve(handlers.me as Response),
    );
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock;
  }

  it("login() navigates to the login endpoint with an encoded returnUrl", async () => {
    mockRoutes({ me: { status: 200, ok: true, json: async () => null } });

    render(
      <AuthProvider>
        <AuthControls />
      </AuthProvider>,
    );
    await screen.findByText("anon");

    await userEvent.click(screen.getByRole("button", { name: "login" }));

    expect(window.location.href).toBe(
      `/auth/login?returnUrl=${encodeURIComponent("/dest")}`,
    );
  });

  it("logout() POSTs, then navigates to the returned end-session URL", async () => {
    mockRoutes({
      me: {
        status: 200,
        ok: true,
        json: async () => ({ sub: "u1", name: "Ada", roles: [], claims: {} }),
      },
      logout: async () => ({
        ok: true,
        json: async () => ({ logoutUrl: "https://kc/realms/snipcache/logout" }),
      }),
    });

    render(
      <AuthProvider>
        <AuthControls />
      </AuthProvider>,
    );
    await screen.findByText("hi Ada");

    await userEvent.click(screen.getByRole("button", { name: "logout" }));

    await waitFor(() =>
      expect(window.location.href).toBe("https://kc/realms/snipcache/logout"),
    );
  });

  it("logout() falls back to clearing state and going home when the server is unreachable", async () => {
    mockRoutes({
      me: {
        status: 200,
        ok: true,
        json: async () => ({ sub: "u1", name: "Ada", roles: [], claims: {} }),
      },
      logout: () => Promise.reject(new Error("network down")),
    });

    render(
      <AuthProvider>
        <AuthControls />
      </AuthProvider>,
    );
    await screen.findByText("hi Ada");

    await userEvent.click(screen.getByRole("button", { name: "logout" }));

    await waitFor(() => expect(window.location.href).toBe("/"));
    expect(screen.getByText("anon")).toBeInTheDocument();
  });
});
