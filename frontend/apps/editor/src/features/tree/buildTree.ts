import type { TreeNode } from "../../api/generated/model";
import { baseName, parentPath } from "../../lib/paths";

export interface TreeFile {
  kind: "file";
  name: string;
  path: string;
}

export interface TreeFolder {
  kind: "dir";
  name: string;
  path: string;
  children: TreeItem[];
}

export type TreeItem = TreeFile | TreeFolder;

// Same order as the site's sidebar (prepare-content): folders first, then
// files, case-insensitive with numbers in natural order. A folder's README.md
// comes first among its files, as its index page does on the site.
const collator = new Intl.Collator("en", { sensitivity: "base", numeric: true });

function compare(a: TreeItem, b: TreeItem): number {
  if (a.kind !== b.kind) return a.kind === "dir" ? -1 : 1;
  const aReadme = isReadme(a);
  if (aReadme !== isReadme(b)) return aReadme ? -1 : 1;
  return collator.compare(a.name, b.name);
}

function isReadme(item: TreeItem): boolean {
  return item.kind === "file" && item.name.toLowerCase() === "readme.md";
}

/**
 * Builds the folder hierarchy from the API's flat node list and returns the
 * root's children. Folders missing from the list are created from the paths
 * of the files inside them.
 */
export function buildTree(nodes: readonly TreeNode[]): TreeItem[] {
  const root: TreeFolder = { kind: "dir", name: "", path: "", children: [] };
  const folders = new Map<string, TreeFolder>([["", root]]);

  function folder(path: string): TreeFolder {
    let existing = folders.get(path);
    if (!existing) {
      existing = { kind: "dir", name: baseName(path), path, children: [] };
      folders.set(path, existing);
      folder(parentPath(path)).children.push(existing);
    }
    return existing;
  }

  for (const node of nodes) {
    if (node.type === "dir") folder(node.path);
    else folder(parentPath(node.path)).children.push({ kind: "file", name: baseName(node.path), path: node.path });
  }

  for (const f of folders.values()) f.children.sort(compare);
  return root.children;
}

/** The folders containing a path, outermost first: `a/b/c.md` → `a`, `a/b`. */
export function ancestorFolders(path: string): string[] {
  const segments = path.split("/").slice(0, -1);
  return segments.map((_, i) => segments.slice(0, i + 1).join("/"));
}
