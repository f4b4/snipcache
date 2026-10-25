import { lazy, Suspense, useEffect, useRef, useState, type ReactNode } from "react";
import { useBlocker } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { pageUrl } from "@snipcache/content-rules";
import {
  getGetFileQueryKey,
  getGetTreeQueryKey,
  useGetFile,
  useSaveFile,
} from "../../api/generated/editor";
import type { FileResponse } from "../../api/generated/model";
import { errorStatus } from "../../api/mutator";
import { Button } from "../../components/Button";
import { encodePath } from "../../lib/paths";
import { ConflictDialog } from "./ConflictDialog";

const CodeEditor = lazy(() => import("./CodeEditor"));

/** The version the edits are based on; its SHA goes with the next save. */
interface Base {
  sha: string;
  content: string;
}

type Status = { kind: "saved"; commitSha: string; commitUrl: string } | { kind: "error"; message: string };

interface EditorPaneProps {
  /** Repository path of the file; the pane is keyed by it, so it never changes. */
  path: string;
  /** Pages URL of the content site, for "View on site". */
  siteBaseUrl: string | undefined;
}

/**
 * One open file: Monaco, the dirty marker and saving. A save sends the SHA of
 * the version the edits started from; a 409 means the file changed on GitHub
 * in between and opens the conflict dialog instead of overwriting.
 */
export function EditorPane({ path, siteBaseUrl }: EditorPaneProps) {
  const apiPath = encodePath(path);
  const queryClient = useQueryClient();
  // Always fetched fresh when a file is opened (gcTime 0) and never refetched
  // behind the user's back while it is open.
  const file = useGetFile(apiPath, {
    query: { gcTime: 0, staleTime: Infinity, refetchOnWindowFocus: false },
  });
  const save = useSaveFile();

  const [base, setBase] = useState<Base | null>(null);
  const [draft, setDraft] = useState("");
  const [status, setStatus] = useState<Status | null>(null);
  const [conflict, setConflict] = useState(false);
  // Bumped when the pane replaces the text (reload), to remount the editor.
  const [editorVersion, setEditorVersion] = useState(0);
  const saving = useRef(false);

  // Adopt the first loaded version as the base (set during render, not in an
  // effect, so the editor never shows an empty draft).
  if (base === null && file.data) {
    setBase({ sha: file.data.sha, content: file.data.content });
    setDraft(file.data.content);
  }

  const dirty = base !== null && draft !== base.content;

  async function saveDraft() {
    if (base === null || !dirty || saving.current) return;
    saving.current = true;
    const content = draft;
    try {
      const result = await save.mutateAsync({
        path: apiPath,
        data: { content, sha: base.sha, message: null },
      });
      setBase({ sha: result.sha, content });
      setStatus({ kind: "saved", commitSha: result.commitSha, commitUrl: result.commitUrl });
      queryClient.setQueryData<FileResponse>(getGetFileQueryKey(apiPath), (old) =>
        old ? { ...old, sha: result.sha, content } : old,
      );
      void queryClient.invalidateQueries({ queryKey: getGetTreeQueryKey() });
    } catch (error) {
      if (errorStatus(error) === 409) {
        setStatus(null);
        setConflict(true);
      } else {
        setStatus({ kind: "error", message: error instanceof Error ? error.message : String(error) });
      }
    } finally {
      saving.current = false;
    }
  }

  async function reload() {
    setConflict(false);
    const fresh = await file.refetch();
    if (fresh.data) {
      setBase({ sha: fresh.data.sha, content: fresh.data.content });
      setDraft(fresh.data.content);
      setEditorVersion((v) => v + 1);
      setStatus(null);
    }
  }

  // Ctrl+S / Cmd+S anywhere on the page, Monaco included (capture phase).
  const saveRef = useRef(saveDraft);
  useEffect(() => {
    saveRef.current = saveDraft;
  });
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === "s") {
        e.preventDefault();
        void saveRef.current();
      }
    };
    window.addEventListener("keydown", onKeyDown, true);
    return () => window.removeEventListener("keydown", onKeyDown, true);
  }, []);

  // Unsaved edits: ask before closing the tab or navigating to another file.
  useEffect(() => {
    if (!dirty) return;
    const onBeforeUnload = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => window.removeEventListener("beforeunload", onBeforeUnload);
  }, [dirty]);
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => dirty && currentLocation.pathname !== nextLocation.pathname,
  );
  useEffect(() => {
    if (blocker.state !== "blocked") return;
    if (window.confirm(`Discard your unsaved changes to ${path}?`)) blocker.proceed();
    else blocker.reset();
  }, [blocker, path]);

  const siteUrl = sitePageUrl(siteBaseUrl, path);

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-wrap items-center gap-2 border-b border-border px-4 py-2">
        <h1 className="min-w-0 flex-1 truncate font-mono text-sm" title={path}>
          {path}
          {dirty && (
            <span className="ml-2 text-brand" aria-label="Unsaved changes" title="Unsaved changes">
              ●
            </span>
          )}
        </h1>
        {siteUrl && (
          <a
            href={siteUrl}
            target="_blank"
            rel="noreferrer"
            className="rounded-md px-2 py-1.5 text-sm text-brand hover:bg-surface-raised"
          >
            View on site ↗
          </a>
        )}
        <Button
          variant="primary"
          onClick={() => void saveDraft()}
          disabled={!dirty || save.isPending}
          title="Save (Ctrl+S)"
        >
          {save.isPending ? "Saving…" : "Save"}
        </Button>
      </div>

      {status && <StatusLine status={status} />}

      <div className="min-h-0 flex-1">
        {file.isPending ? (
          <Centered>Loading {path}…</Centered>
        ) : file.isError && base === null ? (
          <LoadError error={file.error} path={path} onRetry={() => void file.refetch()} />
        ) : (
          <Suspense fallback={<Centered>Loading editor…</Centered>}>
            <CodeEditor key={editorVersion} path={path} initialValue={draft} onChange={setDraft} />
          </Suspense>
        )}
      </div>

      {conflict && <ConflictDialog onReload={() => void reload()} onKeepEditing={() => setConflict(false)} />}
    </div>
  );
}

function StatusLine({ status }: { status: Status }) {
  if (status.kind === "error") {
    return (
      <p role="alert" className="border-b border-border px-4 py-1.5 text-sm text-danger">
        Not saved: {status.message}
      </p>
    );
  }
  return (
    <p role="status" className="border-b border-border px-4 py-1.5 text-sm text-success">
      Saved in commit{" "}
      <a href={status.commitUrl} target="_blank" rel="noreferrer" className="font-mono underline">
        {status.commitSha.slice(0, 7)}
      </a>
      . The site updates in a minute or two.
    </p>
  );
}

function LoadError({ error, path, onRetry }: { error: unknown; path: string; onRetry: () => void }) {
  const status = errorStatus(error);
  const message =
    status === 404
      ? `${path} does not exist. It may have been renamed or deleted on GitHub.`
      : error instanceof Error
        ? error.message
        : "The file could not be loaded.";
  return (
    <Centered>
      <p role="alert" className="max-w-md text-center">
        {message}
      </p>
      {status !== 404 && status !== 413 && status !== 415 && <Button onClick={onRetry}>Retry</Button>}
    </Centered>
  );
}

function Centered({ children }: { children: ReactNode }) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-3 p-6 text-sm text-content-muted">
      {children}
    </div>
  );
}

function sitePageUrl(siteBaseUrl: string | undefined, path: string): string | undefined {
  if (!siteBaseUrl) return undefined;
  try {
    return pageUrl(siteBaseUrl, path);
  } catch {
    return undefined; // a name without any slug characters has no page
  }
}
