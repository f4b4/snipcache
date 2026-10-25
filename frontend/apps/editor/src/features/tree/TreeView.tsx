import type { TreeFolder, TreeItem } from "./buildTree";

interface TreeViewProps {
  items: TreeItem[];
  /** Path of the highlighted node: the open file or a clicked folder. */
  selected: string | undefined;
  /** The open file, marked as the current page. */
  openFile: string | undefined;
  expanded: ReadonlySet<string>;
  /** A folder was clicked: toggle it and make it the target for new items. */
  onFolderClick: (path: string) => void;
  onFileClick: (path: string) => void;
}

/** The repository as a collapsible folder tree. */
export function TreeView(props: TreeViewProps) {
  return (
    <nav aria-label="Files">
      <TreeList {...props} depth={0} />
    </nav>
  );
}

function TreeList({ items, depth, ...props }: TreeViewProps & { depth: number }) {
  return (
    <ul className="m-0 list-none p-0">
      {items.map((item) =>
        item.kind === "dir" ? (
          <FolderRow key={item.path} folder={item} depth={depth} {...props} />
        ) : (
          <li key={item.path}>
            <button
              type="button"
              onClick={() => props.onFileClick(item.path)}
              aria-current={item.path === props.openFile ? "page" : undefined}
              className={rowClass(item.path === props.selected)}
              style={{ paddingLeft: indent(depth) }}
              title={item.path}
            >
              <span aria-hidden="true" className="w-3 shrink-0" />
              <span className="truncate">{item.name}</span>
            </button>
          </li>
        ),
      )}
    </ul>
  );
}

function FolderRow({
  folder,
  depth,
  ...props
}: Omit<TreeViewProps, "items"> & { folder: TreeFolder; depth: number }) {
  const isExpanded = props.expanded.has(folder.path);
  return (
    <li>
      <button
        type="button"
        onClick={() => props.onFolderClick(folder.path)}
        aria-expanded={isExpanded}
        className={`${rowClass(folder.path === props.selected)} font-medium`}
        style={{ paddingLeft: indent(depth) }}
        title={folder.path}
      >
        <span aria-hidden="true" className="w-3 shrink-0 text-xs text-content-muted">
          {isExpanded ? "▾" : "▸"}
        </span>
        <span className="truncate">{folder.name}</span>
      </button>
      {isExpanded && <TreeList items={folder.children} depth={depth + 1} {...props} />}
    </li>
  );
}

function indent(depth: number): string {
  return `${0.5 + depth * 0.875}rem`;
}

function rowClass(selected: boolean): string {
  return `flex w-full cursor-pointer items-center gap-1.5 py-1 pr-2 text-left text-sm ${
    selected ? "bg-selected" : "hover:bg-surface"
  }`;
}
