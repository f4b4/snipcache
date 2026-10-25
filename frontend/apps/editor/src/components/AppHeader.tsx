import { useAuth } from "@snipcache/auth";
import { Button } from "./Button";

interface AppHeaderProps {
  /** `owner/repository` from /api/config, once loaded. */
  repository: string | undefined;
  branch: string | undefined;
}

/** Top bar of the signed-in editor: the content repository and the session. */
export function AppHeader({ repository, branch }: AppHeaderProps) {
  const { user, logout } = useAuth();
  return (
    <header className="flex items-center gap-3 border-b border-border px-4 py-2">
      <span className="font-semibold">SnipCache Editor</span>
      {repository && (
        <a
          href={`https://github.com/${repository}`}
          target="_blank"
          rel="noreferrer"
          className="truncate text-sm text-content-muted hover:text-content"
        >
          {repository}
          {branch && ` · ${branch}`}
        </a>
      )}
      <span className="flex-1" />
      {user && <span className="text-sm text-content-muted">{user.name}</span>}
      <Button variant="ghost" onClick={() => logout()}>
        Sign out
      </Button>
    </header>
  );
}
