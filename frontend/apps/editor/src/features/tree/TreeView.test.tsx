import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { buildTree } from "./buildTree";
import { TreeView } from "./TreeView";

const items = buildTree([
  { path: "SQL", type: "dir", size: null },
  { path: "SQL/count.sql", type: "file", size: 1 },
  { path: "Notes", type: "dir", size: null },
  { path: "Notes/todo.md", type: "file", size: 1 },
  { path: "README.md", type: "file", size: 1 },
]);

function renderTree(expanded: string[], openFile?: string) {
  const handlers = { onFolderClick: vi.fn(), onFileClick: vi.fn() };
  render(
    <TreeView items={items} selected={openFile} openFile={openFile} expanded={new Set(expanded)} {...handlers} />,
  );
  return handlers;
}

describe("TreeView", () => {
  it("shows the top level and hides the content of collapsed folders", () => {
    renderTree([]);

    const rows = screen.getAllByRole("button").map((b) => b.textContent);
    expect(rows).toEqual(["▸Notes", "▸SQL", "README.md"]);
    expect(screen.getByRole("button", { name: /SQL/ })).toHaveAttribute("aria-expanded", "false");
  });

  it("shows the content of expanded folders and marks the open file", () => {
    renderTree(["SQL"], "SQL/count.sql");

    expect(screen.getByRole("button", { name: /SQL/ })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: "count.sql" })).toHaveAttribute("aria-current", "page");
    expect(screen.queryByRole("button", { name: "todo.md" })).not.toBeInTheDocument();
  });

  it("reports folder and file clicks with their paths", async () => {
    const user = userEvent.setup();
    const handlers = renderTree(["SQL"]);

    await user.click(screen.getByRole("button", { name: /Notes/ }));
    await user.click(screen.getByRole("button", { name: "count.sql" }));

    expect(handlers.onFolderClick).toHaveBeenCalledWith("Notes");
    expect(handlers.onFileClick).toHaveBeenCalledWith("SQL/count.sql");
  });
});
