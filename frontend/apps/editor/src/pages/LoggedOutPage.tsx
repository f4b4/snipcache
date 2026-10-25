import { useAuth } from "@snipcache/auth";
import { Button } from "../components/Button";
import { PublicShell } from "../components/PublicShell";

/**
 * Post-logout destination: Keycloak's post_logout_redirect_uri points here
 * (the backend builds `{App:BaseAddress}/logged-out`).
 */
export function LoggedOutPage() {
  const { login } = useAuth();
  return (
    <PublicShell title="Signed out">
      <p className="text-sm text-content-muted">You have been signed out of the SnipCache Editor.</p>
      <Button variant="primary" onClick={() => login("/")}>
        Sign in again
      </Button>
    </PublicShell>
  );
}
