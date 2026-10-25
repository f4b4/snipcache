import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useGetFile, useSaveFile } from "../../api/generated/editor";
import { ApiError } from "../../api/mutator";
import { renderAt } from "../../test/render";
import { EditorPane } from "./EditorPane";

vi.mock("../../api/generated/editor", () => ({
  useGetFile: vi.fn(),
  useSaveFile: vi.fn(),
  getGetFileQueryKey: (path: string) => [`/api/files/${path}`],
  getGetTreeQueryKey: () => ["/api/tree"],
}));

// Monaco cannot run in jsdom; an uncontrolled textarea stands in for it.
vi.mock("./CodeEditor", () => ({
  default: ({ initialValue, onChange }: { initialValue: string; onChange: (v: string) => void }) => (
    <textarea aria-label="Editor" defaultValue={initialValue} onChange={(e) => onChange(e.target.value)} />
  ),
}));

const refetch = vi.fn();
const mutateAsync = vi.fn();

function serveFile(content: string, sha = "sha-1") {
  vi.mocked(useGetFile).mockReturnValue({
    data: { path: "SQL/count.sql", sha, content, encoding: "utf-8" },
    isPending: false,
    isError: false,
    error: null,
    refetch,
  } as unknown as ReturnType<typeof useGetFile>);
}

function renderPane(path = "SQL/count.sql") {
  return renderAt(<EditorPane path={path} siteBaseUrl="https://owner.github.io/content" />, "/edit/x");
}

beforeEach(() => {
  vi.clearAllMocks();
  serveFile("select 1;");
  vi.mocked(useSaveFile).mockReturnValue({ mutateAsync, isPending: false } as unknown as ReturnType<
    typeof useSaveFile
  >);
});

describe("EditorPane", () => {
  it("shows the file, clean, with a link to its page on the site", async () => {
    renderPane();

    expect(await screen.findByLabelText("Editor")).toHaveValue("select 1;");
    expect(screen.queryByLabelText("Unsaved changes")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
    expect(screen.getByRole("link", { name: /view on site/i })).toHaveAttribute(
      "href",
      "https://owner.github.io/content/sql/count-sql/",
    );
  });

  it("requests the file with an encoded path", () => {
    renderPane("KI-Chats/Codeblöcke in Markdown.md");

    expect(useGetFile).toHaveBeenCalledWith("KI-Chats/Codebl%C3%B6cke%20in%20Markdown.md", expect.anything());
  });

  it("marks edits as unsaved and saves them with Ctrl+S and the loaded SHA", async () => {
    const user = userEvent.setup();
    mutateAsync.mockResolvedValue({ path: "SQL/count.sql", sha: "sha-2", commitSha: "abcdef123456", commitUrl: "https://github.com/c" });
    renderPane();

    await user.type(await screen.findByLabelText("Editor"), " -- all");
    expect(screen.getByLabelText("Unsaved changes")).toBeInTheDocument();

    await user.keyboard("{Control>}s{/Control}");

    expect(mutateAsync).toHaveBeenCalledWith({
      path: "SQL/count.sql",
      data: { content: "select 1; -- all", sha: "sha-1", message: null },
    });
    expect(await screen.findByRole("status")).toHaveTextContent("Saved in commit abcdef1");
    expect(screen.queryByLabelText("Unsaved changes")).not.toBeInTheDocument();
  });

  it("sends the new SHA with the next save", async () => {
    const user = userEvent.setup();
    mutateAsync.mockResolvedValue({ path: "SQL/count.sql", sha: "sha-2", commitSha: "c1", commitUrl: "u" });
    renderPane();

    const editor = await screen.findByLabelText("Editor");
    await user.type(editor, "a");
    await user.click(screen.getByRole("button", { name: "Save" }));
    await screen.findByRole("status");
    await user.type(editor, "b");
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(mutateAsync).toHaveBeenLastCalledWith({
      path: "SQL/count.sql",
      data: { content: "select 1;ab", sha: "sha-2", message: null },
    });
  });

  it("on 409 offers to reload, which discards the edits and adopts the current version", async () => {
    const user = userEvent.setup();
    mutateAsync.mockRejectedValue(new ApiError(409, { title: "Conflict" }, "PUT → 409"));
    refetch.mockResolvedValue({ data: { path: "SQL/count.sql", sha: "sha-9", content: "select 2;", encoding: "utf-8" } });
    renderPane();

    await user.type(await screen.findByLabelText("Editor"), "!");
    await user.click(screen.getByRole("button", { name: "Save" }));

    const dialog = await screen.findByRole("dialog", { name: "This file changed on GitHub since you opened it" });
    expect(dialog).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Reload" }));

    await waitFor(() => expect(screen.getByLabelText("Editor")).toHaveValue("select 2;"));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Unsaved changes")).not.toBeInTheDocument();

    // The next save is based on the reloaded version.
    mutateAsync.mockResolvedValue({ path: "SQL/count.sql", sha: "sha-10", commitSha: "c", commitUrl: "u" });
    await user.type(screen.getByLabelText("Editor"), "x");
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect(mutateAsync).toHaveBeenLastCalledWith(
      expect.objectContaining({ data: expect.objectContaining({ sha: "sha-9" }) }),
    );
  });

  it("on 409 Keep editing closes the dialog and keeps the text", async () => {
    const user = userEvent.setup();
    mutateAsync.mockRejectedValue(new ApiError(409, null, "PUT → 409"));
    renderPane();

    await user.type(await screen.findByLabelText("Editor"), "!");
    await user.click(screen.getByRole("button", { name: "Save" }));
    await user.click(await screen.findByRole("button", { name: "Keep editing" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Editor")).toHaveValue("select 1;!");
    expect(refetch).not.toHaveBeenCalled();
  });

  it("reports other save errors inline", async () => {
    const user = userEvent.setup();
    mutateAsync.mockRejectedValue(new ApiError(502, { detail: "GitHub rejected the access token." }, "x"));
    renderPane();

    await user.type(await screen.findByLabelText("Editor"), "!");
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Not saved: GitHub rejected the access token.");
    expect(screen.getByLabelText("Unsaved changes")).toBeInTheDocument();
  });

  it("explains a missing file", () => {
    vi.mocked(useGetFile).mockReturnValue({
      data: undefined,
      isPending: false,
      isError: true,
      error: new ApiError(404, { detail: "x" }, "x"),
      refetch,
    } as unknown as ReturnType<typeof useGetFile>);

    renderPane("gone.md");

    expect(screen.getByRole("alert")).toHaveTextContent("gone.md does not exist.");
  });
});
