import { isMarkdownPath, isReadmePath, pageSlug, pageUrl, slugifySegment } from "./slug.ts";
// Shared with the backend's C# port of the rule (PageSlugsTests).
import cases from "./slug-cases.json" with { type: "json" };

describe("slugifySegment", () => {
  it.each(cases.segments)("%s → %s", (input, expected) => {
    expect(slugifySegment(input)).toBe(expected);
  });

  it.each(cases.invalidSegments)("throws for %s, where nothing is left", (input) => {
    expect(() => slugifySegment(input)).toThrow(`"${input}"`);
  });
});

describe("pageSlug", () => {
  it.each(cases.pages)("%s → %s", (input, expected) => {
    expect(pageSlug(input)).toBe(expected);
  });
});

describe("pageUrl", () => {
  // Joins base URL and slug with a trailing slash, maps the root README to the base URL,
  // percent-encodes non-ASCII slugs.
  it.each(cases.urls)("%s + %s → %s", (base, path, expected) => {
    expect(pageUrl(base, path)).toBe(expected);
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
