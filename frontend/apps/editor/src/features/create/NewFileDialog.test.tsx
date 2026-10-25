import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useSaveFile } from "../../api/generated/editor";
import { ApiError } from "../../api/mutator";
import { renderAt } from "../../test/render";
import { NewFileDialog } from "./NewFileDialog";
import type { ExistingPaths } from "./validation";

vi.mock("../../api/generated/editor", () => ({
  useSaveFile: vi.fn(),
  getGetTreeQueryKey: () => ["/api/tree"],
}));

const mutateAsync = vi.fn();
const existing: ExistingPaths = new Map([
  ["SQL", "dir"],
  ["SQL/count.sql", "file"],
]);

function renderDialog(folder = "SQL") {
  const props = { onClose: vi.fn(), onCreated: vi.fn() };
  renderAt(<NewFileDialog folder={folder} existing={existing} {...props} />);
  return props;
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(useSaveFile).mockReturnValue({ mutateAsync, isPending: false } as unknown as ReturnType<
    typeof useSaveFile
  >);
});

describe("NewFileDialog", () => {
  it("prefills the folder and focuses the name", () => {
    renderDialog();

    expect(screen.getByLabelText("Folder")).toHaveValue("SQL");
    expect(screen.getByLabelText("File name")).toHaveFocus();
  });

  it.each([
    ["a,b.sql", "only letters"],
    ["count.sql", "SQL/count.sql already exists."],
    ["Count.sql", "same address as SQL/count.sql"],
    ["notes", "Add an extension"],
  ])("rejects %j without calling the API", async (name, message) => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText("File name"), name);
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(screen.getByRole("alert")).toHaveTextContent(message);
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("clears the error when the input changes", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText("File name"), "count.sql");
    await user.click(screen.getByRole("button", { name: "Create" }));
    expect(screen.getByRole("alert")).toBeInTheDocument();

    await user.type(screen.getByLabelText("File name"), "x");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("creates a Markdown file seeded with the title and opens it", async () => {
    const user = userEvent.setup();
    mutateAsync.mockResolvedValue({ path: "SQL/Postgres Tipps.md", sha: "s", commitSha: "c", commitUrl: "u" });
    const { onCreated } = renderDialog();

    await user.type(screen.getByLabelText("File name"), "Postgres Tipps.md");
    await user.type(screen.getByLabelText("Title (optional)"), "  Postgres-Tipps ");
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(mutateAsync).toHaveBeenCalledWith({
      path: "SQL/Postgres%20Tipps.md",
      data: { content: "# Postgres-Tipps\n", sha: null, message: null },
    });
    expect(onCreated).toHaveBeenCalledWith("SQL/Postgres Tipps.md");
  });

  it("creates other files empty, in the root when the folder is cleared", async () => {
    const user = userEvent.setup();
    mutateAsync.mockResolvedValue({ path: "backup.ps1", sha: "s", commitSha: "c", commitUrl: "u" });
    const { onCreated } = renderDialog();

    expect(screen.queryByLabelText("Title (optional)")).not.toBeInTheDocument();
    await user.clear(screen.getByLabelText("Folder"));
    await user.type(screen.getByLabelText("File name"), "backup.ps1");
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(mutateAsync).toHaveBeenCalledWith({
      path: "backup.ps1",
      data: { content: "", sha: null, message: null },
    });
    expect(onCreated).toHaveBeenCalledWith("backup.ps1");
  });

  it("reports a file created on GitHub in the meantime", async () => {
    const user = userEvent.setup();
    mutateAsync.mockRejectedValue(new ApiError(409, null, "409"));
    const { onCreated } = renderDialog();

    await user.type(screen.getByLabelText("File name"), "sum.sql");
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("SQL/sum.sql already exists on GitHub.");
    expect(onCreated).not.toHaveBeenCalled();
  });

  it("closes on Escape", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDialog();

    await user.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalled();
  });
});
