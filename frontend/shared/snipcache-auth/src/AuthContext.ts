import { createContext } from "react";

export interface User {
  sub: string;
  name: string;
  email: string;
  roles: string[];
  claims: Record<string, string[]>;
}

export interface AuthState {
  user: User | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  serviceError: boolean;
  /** Starts the Keycloak login flow (full-page redirect). */
  login: (returnUrl?: string) => void;
  /** Clears the server session and redirects to Keycloak end-session. */
  logout: () => void;
}

export const AuthContext = createContext<AuthState | null>(null);
