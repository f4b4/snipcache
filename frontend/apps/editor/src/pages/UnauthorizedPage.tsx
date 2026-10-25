import { useAuth } from "@snipcache/auth";
import { Button } from "../components/Button";
import { PublicShell } from "../components/PublicShell";

/** Signed in, but the API answers 403: the account lacks the role in Auth:RequiredRole. */
export function UnauthorizedPage() {
  const { user, logout } = useAuth();
  return (
    <PublicShell title="No access">
      <p className="text-sm text-content-muted">
        {user ? `${user.name} is signed in but` : "Your account"} may not use this editor. Ask the
        owner for the editor role, or sign in with another account.
      </p>
      <Button onClick={() => logout()}>Sign out</Button>
    </PublicShell>
  );
}
