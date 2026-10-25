import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useCreateFolder } from "../../api/generated/editor";
import { ApiError } from "../../api/mutator";
import { renderAt } from "../../test/render";
import { NewFolderDialog } from "./NewFolderDialog";
import type { ExistingPaths } from "./validation";

vi.mock("../../api/generated/editor", () => ({
  useCreateFolder: vi.fn(),
  getGetTreeQueryKey: () => ["/api/tree"],
}));

const mutateAsync = vi.fn();
const existing: ExistingPaths = new Map([
  ["SQL", "dir"],
  ["SQL/Postgres.md", "file"],
]);

function renderDialog(folder = "SQL") {
  const props = { onClose: vi.fn(), onCreated: vi.fn() };
  renderAt(<NewFolderDialog folder={folder} existing={existing} {...props} />);
  return props;
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(useCreateFolder).mockReturnValue({ mutateAsync, isPending: false } as unknown as ReturnType<
    typeof useCreateFolder
  >);
});

describe("NewFolderDialog", () => {
  it("creates the folder under the parent and opens its README", async () => {
    const user = userEvent.setup();
    mutateAsync.mockResolvedValue({ path: "SQL/MySQL/README.md", sha: "s", commitSha: "c", commitUrl: "u" });
    const { onCreated } = renderDialog();

    await user.type(screen.getByLabelText("Folder name"), "MySQL");
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(mutateAsync).toHaveBeenCalledWith({ data: { path: "SQL/MySQL", title: null } });
    expect(onCreated).toHaveBeenCalledWith("SQL/MySQL/README.md");
  });

  it("passes the title", async () => {
    const user = userEvent.setup();
    mutateAsync.mockResolvedValue({ path: "Shell/README.md", sha: "s", commitSha: "c", commitUrl: "u" });
    renderDialog("");

    await user.type(screen.getByLabelText("Folder name"), "Shell");
    await user.type(screen.getByLabelText("Title (optional)"), "Shell-Rezepte");
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(mutateAsync).toHaveBeenCalledWith({ data: { path: "Shell", title: "Shell-Rezepte" } });
  });

  it.each([
    ["", "The name is required."],
    [".hidden", "must not start with a dot"],
    ["a:b", "only letters"],
  ])("validates %j", async (name, message) => {
    const user = userEvent.setup();
    renderDialog();

    if (name) await user.type(screen.getByLabelText("Folder name"), name);
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(screen.getByRole("alert")).toHaveTextContent(message);
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("rejects a folder whose index page would take an existing page's address", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText("Folder name"), "postgres");
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(screen.getByRole("alert")).toHaveTextContent("same address as SQL/Postgres.md");
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("reports an existing folder from the server", async () => {
    const user = userEvent.setup();
    mutateAsync.mockRejectedValue(new ApiError(409, null, "409"));
    renderDialog();

    await user.type(screen.getByLabelText("Folder name"), "New");
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("SQL/New already exists on GitHub.");
  });
});
