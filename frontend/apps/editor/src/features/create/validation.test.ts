import { describe, expect, it } from "vitest";
import { createPathError, type ExistingPaths } from "./validation";

const existing: ExistingPaths = new Map([
  ["SQL", "dir"],
  ["SQL/count.sql", "file"],
  ["Notes", "dir"],
  ["Notes/README.md", "file"],
  ["Notes/My Note.md", "file"],
  ["v1.2", "dir"],
]);

describe("createPathError", () => {
  it("accepts a new, valid path", () => {
    expect(createPathError("SQL/sum.sql", existing)).toBeUndefined();
    expect(createPathError("Postgres/README.md", existing)).toBeUndefined();
  });

  it("applies the backend's name rules", () => {
    expect(createPathError("SQL/a,b.sql", existing)).toContain("only letters");
  });

  it("rejects an existing path", () => {
    expect(createPathError("SQL/count.sql", existing)).toBe("SQL/count.sql already exists.");
  });

  it("rejects a page slug that is already taken", () => {
    expect(createPathError("Notes/my-note.md", existing)).toBe(
      "The page would get the same address as Notes/My Note.md.",
    );
    expect(createPathError("SQL/Count.sql", existing)).toContain("SQL/count.sql");
  });

  it("does not treat folders as pages", () => {
    expect(createPathError("v1-2.md", existing)).toBeUndefined();
  });

  it("rejects names without a letter or digit", () => {
    expect(createPathError("SQL/().md", existing)).toBe("Every name needs at least one letter or digit.");
  });
});
