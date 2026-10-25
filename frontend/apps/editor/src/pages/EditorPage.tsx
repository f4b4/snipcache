import { useMemo, useState } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { useGetConfig, useGetTree } from "../api/generated/editor";
import { errorStatus } from "../api/mutator";
import { AppHeader } from "../components/AppHeader";
import { Button } from "../components/Button";
import { NewFileDialog } from "../features/create/NewFileDialog";
import { NewFolderDialog } from "../features/create/NewFolderDialog";
import type { ExistingPaths } from "../features/create/validation";
import { EditorPane } from "../features/editor/EditorPane";
import { ancestorFolders, buildTree } from "../features/tree/buildTree";
import { TreeView } from "../features/tree/TreeView";
import { editRoute, parentPath, pathFromRoute } from "../lib/paths";

/**
 * The signed-in editor at `/` and `/edit/<path>`: the folder tree on the left,
 * the open file on the right. The route is the single source of the open file,
 * so deep links from the site and the browser history just work.
 */
export function EditorPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const openFile = pathFromRoute(location.pathname);

  const config = useGetConfig();
  const tree = useGetTree();
  const nodes = tree.data?.nodes;
  const items = useMemo(() => buildTree(nodes ?? []), [nodes]);
  const existing: ExistingPaths = useMemo(() => new Map(nodes?.map((n) => [n.path, n.type])), [nodes]);

  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  const [selectedFolder, setSelectedFolder] = useState<string>();
  const [dialog, setDialog] = useState<"file" | "folder">();

  // Reveal each newly opened file in the tree (adjusted during render).
  const [revealed, setRevealed] = useState<string>();
  if (openFile !== revealed) {
    setRevealed(openFile);
    setSelectedFolder(undefined);
    if (openFile) expand(ancestorFolders(openFile));
  }

  function expand(paths: string[]) {
    setExpanded((prev) => (paths.every((p) => prev.has(p)) ? prev : new Set([...prev, ...paths])));
  }

  function toggleFolder(path: string) {
    setSelectedFolder(path);
    setExpanded((prev) => {
      const next = new Set(prev);
      if (!next.delete(path)) next.add(path);
      return next;
    });
  }

  function created(path: string) {
    setDialog(undefined);
    expand(ancestorFolders(path));
    navigate(editRoute(path));
  }

  if (errorStatus(config.error) === 403 || errorStatus(tree.error) === 403) {
    return <Navigate to="/unauthorized" replace />;
  }

  // New items go into the clicked folder, else next to the open file.
  const targetFolder = selectedFolder ?? (openFile ? parentPath(openFile) : "");

  return (
    <div className="flex h-full flex-col">
      <AppHeader repository={config.data?.repository} branch={config.data?.branch} />
      <div className="flex min-h-0 flex-1">
        <aside className="flex w-72 shrink-0 flex-col border-r border-border bg-surface-raised">
          <div className="flex gap-2 border-b border-border p-2">
            <Button onClick={() => setDialog("file")} disabled={!nodes}>
              New file
            </Button>
            <Button onClick={() => setDialog("folder")} disabled={!nodes}>
              New folder
            </Button>
          </div>
          <div className="min-h-0 flex-1 overflow-auto py-1">
            {tree.isPending ? (
              <p className="p-3 text-sm text-content-muted">Loading…</p>
            ) : tree.isError ? (
              <div className="flex flex-col items-start gap-2 p-3 text-sm">
                <p role="alert" className="text-danger">
                  {tree.error instanceof Error ? tree.error.message : "The tree could not be loaded."}
                </p>
                <Button onClick={() => void tree.refetch()}>Retry</Button>
              </div>
            ) : (
              <TreeView
                items={items}
                selected={selectedFolder ?? openFile}
                openFile={openFile}
                expanded={expanded}
                onFolderClick={toggleFolder}
                onFileClick={(path) => navigate(editRoute(path))}
              />
            )}
          </div>
        </aside>
        <main className="min-w-0 flex-1">
          {openFile ? (
            <EditorPane key={openFile} path={openFile} siteBaseUrl={config.data?.siteBaseUrl} />
          ) : (
            <div className="flex h-full items-center justify-center p-6 text-sm text-content-muted">
              Open a file from the tree, or create a new one.
            </div>
          )}
        </main>
      </div>

      {dialog === "file" && (
        <NewFileDialog folder={targetFolder} existing={existing} onClose={() => setDialog(undefined)} onCreated={created} />
      )}
      {dialog === "folder" && (
        <NewFolderDialog folder={targetFolder} existing={existing} onClose={() => setDialog(undefined)} onCreated={created} />
      )}
    </div>
  );
}
