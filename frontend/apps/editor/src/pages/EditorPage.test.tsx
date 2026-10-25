import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useGetConfig, useGetTree } from "../api/generated/editor";
import { ApiError } from "../api/mutator";
import { renderAt } from "../test/render";
import { EditorPage } from "./EditorPage";

vi.mock("../api/generated/editor", () => ({
  useGetConfig: vi.fn(),
  useGetTree: vi.fn(),
  useSaveFile: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useCreateFolder: () => ({ mutateAsync: vi.fn(), isPending: false }),
  getGetTreeQueryKey: () => ["/api/tree"],
}));

// The pane has its own tests; here it only shows which file is open.
vi.mock("../features/editor/EditorPane", () => ({
  EditorPane: ({ path }: { path: string }) => <p>editing {path}</p>,
}));

beforeEach(() => {
  vi.mocked(useGetConfig).mockReturnValue({
    data: { siteBaseUrl: "https://owner.github.io/content", repository: "owner/content", branch: "main" },
    error: null,
  } as unknown as ReturnType<typeof useGetConfig>);
  vi.mocked(useGetTree).mockReturnValue({
    data: {
      nodes: [
        { path: "KI-Chats", type: "dir", size: null },
        { path: "KI-Chats/Codeblöcke in Markdown.md", type: "file", size: 10 },
        { path: "README.md", type: "file", size: 7 },
        { path: "SQL", type: "dir", size: null },
        { path: "SQL/Postgres", type: "dir", size: null },
        { path: "SQL/Postgres/vacuum.sql", type: "file", size: 3 },
      ],
    },
    isPending: false,
    isError: false,
    error: null,
  } as unknown as ReturnType<typeof useGetTree>);
});

describe("EditorPage", () => {
  it("renders the tree from the flat list, collapsed, with a hint instead of an editor", () => {
    renderAt(<EditorPage />, "/");

    expect(screen.getByRole("button", { name: /KI-Chats/ })).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("button", { name: /SQL/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "README.md" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "vacuum.sql" })).not.toBeInTheDocument();
    expect(screen.getByText(/open a file from the tree/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "owner/content · main" })).toBeInTheDocument();
  });

  it("expands and collapses folders, and opens a file on click", async () => {
    const user = userEvent.setup();
    const { router } = renderAt(<EditorPage />, "/");

    await user.click(screen.getByRole("button", { name: /SQL/ }));
    await user.click(screen.getByRole("button", { name: /Postgres/ }));
    await user.click(screen.getByRole("button", { name: "vacuum.sql" }));

    expect(router.state.location.pathname).toBe("/edit/SQL/Postgres/vacuum.sql");
    expect(screen.getByText("editing SQL/Postgres/vacuum.sql")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "SQL" }));
    expect(screen.queryByRole("button", { name: "vacuum.sql" })).not.toBeInTheDocument();
  });

  it("opens a deep link and reveals the file in the tree", () => {
    renderAt(<EditorPage />, "/edit/KI-Chats/Codebl%C3%B6cke%20in%20Markdown.md");

    expect(screen.getByText("editing KI-Chats/Codeblöcke in Markdown.md")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Codeblöcke in Markdown.md" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });

  it("prefills the new file dialog with the selected folder", async () => {
    const user = userEvent.setup();
    renderAt(<EditorPage />, "/edit/README.md");

    await user.click(screen.getByRole("button", { name: /SQL/ }));
    await user.click(screen.getByRole("button", { name: "New file" }));

    expect(screen.getByLabelText("Folder")).toHaveValue("SQL");
  });

  it("sends users without the editor role to the unauthorized page", () => {
    vi.mocked(useGetTree).mockReturnValue({
      data: undefined,
      isPending: false,
      isError: true,
      error: new ApiError(403, null, "403"),
    } as unknown as ReturnType<typeof useGetTree>);

    renderAt(<EditorPage />, "/");

    expect(screen.getByText("unauthorized page")).toBeInTheDocument();
  });
});
