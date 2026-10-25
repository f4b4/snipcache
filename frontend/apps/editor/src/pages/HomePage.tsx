import { useEffect } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useAuth } from "@snipcache/auth";
import { Button } from "../components/Button";
import { PublicShell } from "../components/PublicShell";

// Remembers when sign-in was last started automatically, so a session that
// does not stick (e.g. cookies blocked) shows the button instead of looping.
const AUTO_LOGIN_KEY = "snipcache-auto-login";
const AUTO_LOGIN_PAUSE_MS = 30_000;

function mayAutoLogin(): boolean {
  try {
    const last = Number(sessionStorage.getItem(AUTO_LOGIN_KEY) ?? 0);
    if (Date.now() - last < AUTO_LOGIN_PAUSE_MS) return false;
    sessionStorage.setItem(AUTO_LOGIN_KEY, String(Date.now()));
    return true;
  } catch {
    return false;
  }
}

/**
 * Sign-in landing. ProtectedRoute sends visitors without a session here with
 * the page they wanted as `returnUrl`; sign-in then starts right away, so an
 * "Edit" link from the site goes straight to Keycloak and back to the file.
 * Opened directly, the page offers a button instead.
 */
export function HomePage() {
  const { isAuthenticated, isLoading, login } = useAuth();
  const location = useLocation();
  const returnUrl = (location.state as { returnUrl?: string } | null)?.returnUrl;
  const autoLogin = !isLoading && !isAuthenticated && returnUrl !== undefined;

  useEffect(() => {
    if (autoLogin && mayAutoLogin()) login(returnUrl);
  }, [autoLogin, login, returnUrl]);

  if (isAuthenticated) return <Navigate to={returnUrl ?? "/"} replace />;

  return (
    <PublicShell title="SnipCache Editor">
      <p className="text-sm text-content-muted">
        Edit the snippets of your SnipCache site. Every save is a commit to the content repository.
      </p>
      <Button variant="primary" onClick={() => login(returnUrl ?? "/")} disabled={isLoading}>
        Sign in
      </Button>
    </PublicShell>
  );
}
