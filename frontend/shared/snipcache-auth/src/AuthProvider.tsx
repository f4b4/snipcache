import { useEffect, useState } from "react";
import type { CSSProperties, ReactNode } from "react";
import { AuthContext, type User } from "./AuthContext";
import { configureAuth, getAuthEndpoints, type AuthEndpoints } from "./config";

// The default service-error fallback. Styled with inline CSS system-color
// keywords (Canvas/CanvasText/ButtonFace…) so it is self-contained, automatically
// light/dark-aware, and carries no app-specific colors. Apps that want branded
// UI pass `renderServiceError` instead.
const overlayStyle: CSSProperties = {
  display: "flex",
  minHeight: "100dvh",
  flexDirection: "column",
  alignItems: "center",
  justifyContent: "center",
  gap: "1rem",
  padding: "0 1rem",
  textAlign: "center",
  background: "Canvas",
  color: "CanvasText",
  fontFamily: "system-ui, sans-serif",
};

const titleStyle: CSSProperties = { fontSize: "1.25rem", fontWeight: 600 };
const subtitleStyle: CSSProperties = { fontSize: "0.875rem", color: "GrayText" };
const buttonStyle: CSSProperties = {
  marginTop: "0.5rem",
  cursor: "pointer",
  borderRadius: "0.375rem",
  border: "1px solid GrayText",
  background: "ButtonFace",
  color: "ButtonText",
  padding: "0.5rem 1rem",
  fontSize: "0.875rem",
};

function DefaultServiceError() {
  return (
    <div role="alert" style={overlayStyle}>
      <p style={titleStyle}>Service unavailable</p>
      <p style={subtitleStyle}>
        The server is not reachable. Please try again in a moment.
      </p>
      <button
        type="button"
        style={buttonStyle}
        onClick={() => window.location.reload()}
      >
        Retry
      </button>
    </div>
  );
}

export interface AuthProviderProps {
  children: ReactNode;
  /** Overrides one or more `/auth/*` paths; applied via configureAuth(). */
  endpoints?: Partial<AuthEndpoints>;
  /** Replaces the built-in fallback shown when /auth/me returns ≥500. */
  renderServiceError?: () => ReactNode;
}

/**
 * Holds the signed-in user, learned from the BFF's /auth/me on startup. The
 * browser never sees a token — the session lives in an HttpOnly cookie, so all
 * we can observe is "who am I" via /auth/me. Ported from BlondiApp's provider,
 * generalized so the service-error UI and endpoint paths are configurable.
 */
export function AuthProvider({
  children,
  endpoints,
  renderServiceError,
}: AuthProviderProps) {
  // Apply endpoint overrides before the bootstrap effect reads them. The
  // assignment is idempotent, so re-running on re-render is harmless.
  if (endpoints) configureAuth(endpoints);

  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [serviceError, setServiceError] = useState(false);

  useEffect(() => {
    fetch(getAuthEndpoints().me, { credentials: "include" })
      .then((res) => {
        if (res.status >= 500) {
          setServiceError(true);
          return null;
        }
        return res.ok ? res.json() : null;
      })
      .then((data) => setUser(data))
      .catch(() => setUser(null))
      .finally(() => setIsLoading(false));
  }, []);

  const login = (returnUrl?: string) => {
    const destination =
      returnUrl ?? window.location.pathname + window.location.search;
    window.location.href = `${getAuthEndpoints().login}?returnUrl=${encodeURIComponent(destination)}`;
  };

  const logout = async () => {
    try {
      const res = await fetch(getAuthEndpoints().logout, {
        method: "POST",
        credentials: "include",
      });
      const { logoutUrl } = await res.json();
      window.location.href = logoutUrl; // full-page nav — no CORS issue
    } catch {
      // Server unreachable — clear local state and go home.
      setUser(null);
      window.location.href = "/";
    }
  };

  if (serviceError) {
    return <>{renderServiceError ? renderServiceError() : <DefaultServiceError />}</>;
  }

  return (
    <AuthContext.Provider
      value={{
        user,
        isLoading,
        isAuthenticated: !!user,
        serviceError,
        login,
        logout,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}
