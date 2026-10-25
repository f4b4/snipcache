import { Navigate, useLocation } from "react-router-dom";
import type { CSSProperties, ReactNode } from "react";
import { useAuth } from "./useAuth";

// Default loading fallback — inline CSS system colors, theme-aware and
// self-contained (see AuthProvider for the same rationale).
const loadingStyle: CSSProperties = {
  display: "flex",
  minHeight: "100dvh",
  alignItems: "center",
  justifyContent: "center",
  background: "Canvas",
  color: "GrayText",
  fontFamily: "system-ui, sans-serif",
};

export interface ProtectedRouteProps {
  children: ReactNode;
  /** Roles, any one of which grants access. Omit to require only auth. */
  requiredRoles?: string[];
  /** Where unauthenticated visitors are sent. Default `"/home"`. */
  redirectTo?: string;
  /** Where authenticated-but-unauthorized visitors are sent. Default `"/unauthorized"`. */
  unauthorizedTo?: string;
  /** Shown while the auth bootstrap is in flight. Default: built-in fallback. */
  loadingFallback?: ReactNode;
}

/**
 * Gates a route behind authentication. Unauthenticated visitors are sent to
 * `redirectTo` (a landing page that offers sign-in), with the intended
 * destination carried in router state so the landing page can return there
 * after login. `requiredRoles` additionally gates on role membership.
 */
export function ProtectedRoute({
  children,
  requiredRoles,
  redirectTo = "/home",
  unauthorizedTo = "/unauthorized",
  loadingFallback,
}: ProtectedRouteProps) {
  const { isAuthenticated, isLoading, user } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return <>{loadingFallback ?? <div style={loadingStyle}>Loading…</div>}</>;
  }

  if (!isAuthenticated) {
    const returnUrl = location.pathname + location.search;
    return <Navigate to={redirectTo} replace state={{ returnUrl }} />;
  }

  if (
    requiredRoles?.length &&
    !requiredRoles.some((r) => user?.roles.includes(r))
  ) {
    return <Navigate to={unauthorizedTo} replace />;
  }

  return <>{children}</>;
}
