import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { ProtectedRoute } from "./ProtectedRoute";
import { AuthContext, type AuthState, type User } from "./AuthContext";

function makeState(overrides: Partial<AuthState>): AuthState {
  return {
    user: null,
    isLoading: false,
    isAuthenticated: false,
    serviceError: false,
    login: () => {},
    logout: () => {},
    ...overrides,
  };
}

function makeUser(roles: string[]): User {
  return {
    sub: "u1",
    name: "Test",
    email: "test@example.com",
    roles,
    claims: {},
  };
}

function renderGated(state: AuthState, requiredRoles?: string[]) {
  // Pass requiredRoles only when present — exactOptionalPropertyTypes rejects
  // an explicit `undefined` on the optional prop.
  const roleProps = requiredRoles ? { requiredRoles } : {};
  return render(
    <AuthContext.Provider value={state}>
      <MemoryRouter initialEntries={["/secret"]}>
        <Routes>
          <Route path="/home" element={<p>home page</p>} />
          <Route path="/unauthorized" element={<p>no access</p>} />
          <Route
            path="/secret"
            element={
              <ProtectedRoute {...roleProps}>
                <p>secret content</p>
              </ProtectedRoute>
            }
          />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  );
}

describe("ProtectedRoute", () => {
  it("redirects unauthenticated users to the redirect target", () => {
    renderGated(makeState({ isAuthenticated: false }));
    expect(screen.getByText("home page")).toBeInTheDocument();
    expect(screen.queryByText("secret content")).not.toBeInTheDocument();
  });

  it("renders children for an authenticated user", () => {
    renderGated(makeState({ isAuthenticated: true, user: makeUser([]) }));
    expect(screen.getByText("secret content")).toBeInTheDocument();
  });

  it("shows a loading fallback while the bootstrap is in flight", () => {
    renderGated(makeState({ isLoading: true }));
    expect(screen.getByText("Loading…")).toBeInTheDocument();
    expect(screen.queryByText("secret content")).not.toBeInTheDocument();
  });

  it("sends an authenticated user without a required role to unauthorized", () => {
    renderGated(
      makeState({ isAuthenticated: true, user: makeUser(["viewer"]) }),
      ["admin"],
    );
    expect(screen.getByText("no access")).toBeInTheDocument();
    expect(screen.queryByText("secret content")).not.toBeInTheDocument();
  });

  it("renders children when the user holds a required role", () => {
    renderGated(
      makeState({ isAuthenticated: true, user: makeUser(["admin"]) }),
      ["admin"],
    );
    expect(screen.getByText("secret content")).toBeInTheDocument();
  });
});
