import { describe, expect, it } from "vitest";
import type { TreeNode } from "../../api/generated/model";
import { ancestorFolders, buildTree, type TreeItem } from "./buildTree";

const dir = (path: string): TreeNode => ({ path, type: "dir", size: null });
const file = (path: string): TreeNode => ({ path, type: "file", size: 1 });

// name, with "/" after folders, nested children indented.
function outline(items: TreeItem[], indent = ""): string[] {
  return items.flatMap((item) =>
    item.kind === "dir"
      ? [`${indent}${item.name}/`, ...outline(item.children, `${indent}  `)]
      : [`${indent}${item.name}`],
  );
}

describe("buildTree", () => {
  it("nests the flat list into folders", () => {
    const tree = buildTree([dir("SQL"), file("SQL/count.sql"), file("README.md")]);

    expect(tree[0]).toMatchObject({ kind: "dir", name: "SQL", path: "SQL" });
    expect(outline(tree)).toEqual(["SQL/", "  count.sql", "README.md"]);
  });

  it("puts folders first, README.md first among files, then sorts naturally ignoring case", () => {
    const tree = buildTree([
      file("b.md"),
      file("README.md"),
      file("A.md"),
      file("note 10.md"),
      file("note 9.md"),
      dir("zeta"),
      dir("Alpha"),
    ]);

    expect(outline(tree)).toEqual([
      "Alpha/",
      "zeta/",
      "README.md",
      "A.md",
      "b.md",
      "note 9.md",
      "note 10.md",
    ]);
  });

  it("creates folders that only appear in file paths", () => {
    const tree = buildTree([file("a/b/c.ps1")]);

    expect(outline(tree)).toEqual(["a/", "  b/", "    c.ps1"]);
    expect(tree[0]).toMatchObject({ path: "a", children: [{ path: "a/b" }] });
  });

  it("returns an empty list for an empty repository", () => {
    expect(buildTree([])).toEqual([]);
  });
});

describe("ancestorFolders", () => {
  it("lists the containing folders outermost first", () => {
    expect(ancestorFolders("a/b/c.md")).toEqual(["a", "a/b"]);
    expect(ancestorFolders("README.md")).toEqual([]);
  });
});
