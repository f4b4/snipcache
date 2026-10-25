import { describe, expect, it } from "vitest";
import {
  editRoute,
  encodePath,
  joinPath,
  newPathError,
  normalizeFolder,
  parentPath,
  pathFromRoute,
} from "./paths";

describe("encodePath / editRoute / pathFromRoute", () => {
  it("encodes each segment but keeps the separators", () => {
    expect(encodePath("KI-Chats/Codeblöcke in Markdown.md")).toBe(
      "KI-Chats/Codebl%C3%B6cke%20in%20Markdown.md",
    );
    expect(encodePath("a/#1?.md")).toBe("a/%231%3F.md");
  });

  it("round-trips a path through the edit route", () => {
    const path = "AI/WSL2, VS Code & more.md";
    expect(pathFromRoute(editRoute(path))).toBe(path);
  });

  it("returns undefined for routes that are not an edit route", () => {
    expect(pathFromRoute("/")).toBeUndefined();
    expect(pathFromRoute("/edit/")).toBeUndefined();
    expect(pathFromRoute("/home")).toBeUndefined();
  });

  it("keeps a segment with a stray % as written", () => {
    expect(pathFromRoute("/edit/a/100%.md")).toBe("a/100%.md");
  });
});

describe("folder helpers", () => {
  it("splits and joins at the root and below", () => {
    expect(parentPath("README.md")).toBe("");
    expect(parentPath("a/b/c.md")).toBe("a/b");
    expect(joinPath("", "x.md")).toBe("x.md");
    expect(joinPath("a/b", "x.md")).toBe("a/b/x.md");
  });

  it("normalizes folder input", () => {
    expect(normalizeFolder("  /SQL\\Postgres/ ")).toBe("SQL/Postgres");
  });
});

describe("newPathError", () => {
  it("accepts names the backend accepts", () => {
    expect(newPathError("KI-Chats/Codeblöcke (alt) v1.2.md")).toBeUndefined();
    expect(newPathError("SQL/count_rows.sql")).toBeUndefined();
  });

  it.each([
    ["", "required"],
    ["a//b.md", "empty segments"],
    [".github/x.yml", "must not start with a dot"],
    ["a/../b.md", "must not start with a dot"],
    [" a.md", "whitespace"],
    ["a,b.md", "only letters"],
    ["a/b:c.md", "only letters"],
  ])("rejects %j", (path, message) => {
    expect(newPathError(path)).toContain(message);
  });
});
