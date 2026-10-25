import { isMarkdownPath, isReadmePath, pageSlug, pageUrl, slugifySegment } from "./slug.ts";

describe("slugifySegment", () => {
  it.each([
    ["Notes", "notes"],
    ["count.sql", "count-sql"],
    ["My Recipes", "my-recipes"],
    ["Übersicht Größen", "uebersicht-groessen"],
    ["Ärger ÖL", "aerger-oel"],
    ["café crème", "cafe-creme"],
    ["a - b", "a-b"],
    ["(draft) idea!", "draft-idea"],
    ["snake_case-name", "snake_case-name"],
    [".hidden.", "hidden"],
    ["日本語", "日本語"],
  ])("%s → %s", (input, expected) => {
    expect(slugifySegment(input)).toBe(expected);
  });

  it("throws when nothing is left", () => {
    expect(() => slugifySegment("!!!")).toThrow(/"!!!"/);
  });
});

describe("pageSlug", () => {
  it.each([
    ["README.md", ""],
    ["readme.md", ""],
    ["a/b/README.md", "a/b"],
    ["a/b/note.md", "a/b/note"],
    ["a/b/Note.MD", "a/b/note"],
    ["a/b/count.sql", "a/b/count-sql"],
    ["Shell Scripts/Backup Home.sh", "shell-scripts/backup-home-sh"],
    ["Rezepte/Käse Spätzle.md", "rezepte/kaese-spaetzle"],
    ["a/notes.mdx", "a/notes-mdx"],
    ["Dockerfile", "dockerfile"],
  ])("%s → %s", (input, expected) => {
    expect(pageSlug(input)).toBe(expected);
  });
});

describe("pageUrl", () => {
  it("joins base URL and slug with a trailing slash", () => {
    expect(pageUrl("https://f4b4.github.io/snipcache-content-f4b4/", "sql/count.sql")).toBe(
      "https://f4b4.github.io/snipcache-content-f4b4/sql/count-sql/",
    );
  });

  it("maps the root README to the base URL", () => {
    expect(pageUrl("https://example.org", "README.md")).toBe("https://example.org/");
  });

  it("percent-encodes non-ASCII slugs", () => {
    expect(pageUrl("https://example.org", "日本.md")).toBe("https://example.org/%E6%97%A5%E6%9C%AC/");
  });
});

describe("path predicates", () => {
  it("recognises Markdown and README paths", () => {
    expect(isMarkdownPath("a/x.md")).toBe(true);
    expect(isMarkdownPath("a/x.mdx")).toBe(false);
    expect(isReadmePath("a/README.md")).toBe(true);
    expect(isReadmePath("a/README.txt")).toBe(false);
  });
});
