// Provider + hook
export { AuthProvider } from "./AuthProvider";
export type { AuthProviderProps } from "./AuthProvider";
export { useAuth } from "./useAuth";

// Context + types
export { AuthContext } from "./AuthContext";
export type { AuthState, User } from "./AuthContext";

// Route gate
export { ProtectedRoute } from "./ProtectedRoute";
export type { ProtectedRouteProps } from "./ProtectedRoute";

// BFF fetch client (Orval's generated fetcher calls these)
export { api, apiFetch, redirectToLogin, UnauthenticatedError } from "./client";

// Endpoint configuration
export {
  configureAuth,
  getAuthEndpoints,
  DEFAULT_AUTH_ENDPOINTS,
} from "./config";
export type { AuthEndpoints } from "./config";
