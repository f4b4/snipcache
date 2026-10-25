import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useAuth } from "./useAuth";
import { AuthContext, type AuthState } from "./AuthContext";

function Consumer() {
  const { isAuthenticated } = useAuth();
  return <span>{isAuthenticated ? "in" : "out"}</span>;
}

describe("useAuth", () => {
  it("throws when used outside an AuthProvider", () => {
    // React logs the thrown render error; silence it for a clean test run.
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => render(<Consumer />)).toThrow(
      "useAuth must be used within AuthProvider",
    );
    spy.mockRestore();
  });

  it("returns the context value when inside a provider", () => {
    const state: AuthState = {
      user: null,
      isLoading: false,
      isAuthenticated: true,
      serviceError: false,
      login: () => {},
      logout: () => {},
    };
    render(
      <AuthContext.Provider value={state}>
        <Consumer />
      </AuthContext.Provider>,
    );
    expect(screen.getByText("in")).toBeInTheDocument();
  });
});
