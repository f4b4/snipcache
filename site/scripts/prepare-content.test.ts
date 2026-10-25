import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { parse as parseYaml } from "yaml";
import {
  buildPages,
  buildSidebar,
  fenceFor,
  languageFor,
  normalizeBase,
  prepareContent,
  resolveMarkdownTitle,
  scanContent,
  splitFrontmatter,
  type ContentFile,
  type Page,
} from "./prepare-content.ts";

function frontmatterOf(page: Page): Record<string, any> {
  return parseYaml(/^---\n([\s\S]*?)\n---\n/.exec(page.document)![1]!);
}

function bodyOf(page: Page): string {
  return page.document.replace(/^---\n[\s\S]*?\n---\n\n/, "");
}

function pageFor(sourcePath: string, text: string, options = {}): Page {
  return buildPages([{ sourcePath, text }], options).pages.find((p) => p.sourcePath === sourcePath)!;
}

describe("resolveMarkdownTitle", () => {
  it("prefers the frontmatter title and keeps the H1", () => {
    const result = resolveMarkdownTitle("a/x.md", { title: "From frontmatter" }, "# Heading\n\nText\n");
    expect(result).toEqual({ title: "From frontmatter", body: "# Heading\n\nText\n" });
  });

  it("takes the first H1 and removes it from the body", () => {
    const result = resolveMarkdownTitle("a/x.md", {}, "\n# Title here #\n\nText\n\n# Second\n");
    expect(result.title).toBe("Title here");
    expect(result.body).toBe("Text\n\n# Second\n");
  });

  it("ignores H1 lines inside code fences and H2 lines", () => {
    const body = "## Sub\n\n~~~sh\n# comment\n~~~\n\n````md\n```\n# nested\n```\n````\n\n# Real\n";
    expect(resolveMarkdownTitle("a/x.md", {}, body).title).toBe("Real");
  });

  it("does not treat #hashtag as a heading", () => {
    expect(resolveMarkdownTitle("a/note.md", {}, "#hashtag\n").title).toBe("note");
  });

  it("falls back to the file name without extension", () => {
    expect(resolveMarkdownTitle("a/My Note.md", {}, "Just text.\n")).toEqual({
      title: "My Note",
      body: "Just text.\n",
    });
  });

  it("falls back to the folder name for a README", () => {
    expect(resolveMarkdownTitle("Tools/Git/README.md", {}, "Text\n").title).toBe("Git");
  });

  it("ignores an empty frontmatter title", () => {
    expect(resolveMarkdownTitle("a/x.md", { title: " " }, "# H\n").title).toBe("H");
  });
});

describe("splitFrontmatter", () => {
  it("parses YAML frontmatter", () => {
    expect(splitFrontmatter("---\ntitle: T\n---\nBody\n")).toEqual({ frontmatter: { title: "T" }, body: "Body\n" });
  });

  it("accepts an empty block and CRLF line endings", () => {
    expect(splitFrontmatter("---\n---\nBody")).toEqual({ frontmatter: {}, body: "Body" });
    expect(splitFrontmatter("---\r\ntitle: T\r\n---\r\nBody")).toEqual({ frontmatter: { title: "T" }, body: "Body" });
  });

  it("does not close the block on a value ending in ---", () => {
    expect(splitFrontmatter("---\ntitle: a---\n---\nBody").frontmatter).toEqual({ title: "a---" });
  });

  it("leaves text without frontmatter alone", () => {
    expect(splitFrontmatter("Text\n---\n")).toEqual({ frontmatter: {}, body: "Text\n---\n" });
  });

  it("reports invalid YAML and drops the block", () => {
    const result = splitFrontmatter("---\ntitle: [unclosed\n---\nBody");
    expect(result.body).toBe("Body");
    expect(result.frontmatter).toEqual({});
    expect(result.error).toMatch(/invalid frontmatter/);
  });
});

describe("fenceFor", () => {
  it.each([
    ["no backticks", "```"],
    ["inline `code`", "```"],
    ["```\nfenced\n```", "````"],
    ["`````five", "``````"],
  ])("%j → %s", (content, fence) => {
    expect(fenceFor(content)).toBe(fence);
  });
});

describe("languageFor", () => {
  it.each([
    ["Backup.ps1", "powershell"],
    ["Module.PSM1", "powershell"],
    ["run.sh", "bash"],
    ["x.zsh", "bash"],
    ["clean.bat", "batch"],
    ["clean.cmd", "batch"],
    ["q.sql", "sql"],
    ["c.yml", "yaml"],
    ["App.csproj", "xml"],
    ["main.py", "python"],
    ["Program.cs", "csharp"],
    ["Dockerfile", "dockerfile"],
    ["notes.txt", "text"],
    ["page.mdx", "text"],
    ["LICENSE", "text"],
    [".bashrc", "text"],
  ])("%s → %s", (fileName, language) => {
    expect(languageFor(fileName)).toBe(language);
  });
});

describe("buildPages", () => {
  it("maps READMEs to index pages", () => {
    const { pages } = buildPages([
      { sourcePath: "README.md", text: "# Home\n" },
      { sourcePath: "a/b/README.md", text: "# B\n" },
    ]);
    expect(pages.map((p) => [p.outputPath, p.slug])).toEqual([
      ["index.md", ""],
      ["a/b/index.md", "a/b"],
    ]);
    expect(frontmatterOf(pages[0]!)).not.toHaveProperty("slug");
    expect(frontmatterOf(pages[1]!).slug).toBe("a/b");
  });

  it("generates a home page when the root has no README", () => {
    const { pages } = buildPages([{ sourcePath: "a.md", text: "x" }], { siteTitle: "My Snips" });
    const home = pages.find((p) => p.slug === "")!;
    expect(home.outputPath).toBe("index.md");
    expect(frontmatterOf(home)).toEqual({ title: "My Snips" });
  });

  it("writes Markdown pages with title, slug and the snipcache object", () => {
    const page = pageFor("Git/Undo it.md", "---\ntitle: Undo\ndescription: D\nsidebar: { order: 1 }\n---\nBody\n", {
      base: "/content/",
    });
    expect(page.outputPath).toBe("Git/Undo it.md");
    expect(frontmatterOf(page)).toEqual({
      title: "Undo",
      description: "D",
      slug: "git/undo-it",
      snipcache: {
        sourcePath: "Git/Undo it.md",
        rawUrl: "/content/raw/Git/Undo%20it.md",
        kind: "markdown",
        language: "markdown",
      },
    });
    expect(bodyOf(page)).toBe("Body\n");
  });

  it("adds an edit URL only when an editor is configured", () => {
    expect(frontmatterOf(pageFor("a/ü.md", "x"))).not.toHaveProperty("editUrl");
    expect(frontmatterOf(pageFor("a/ü.md", "x", { editorUrl: "https://ed.example/" })).editUrl).toBe(
      "https://ed.example/edit/a/%C3%BC.md",
    );
  });

  it("wraps other files in one titled code block with a long enough fence", () => {
    const page = pageFor("sql/count.sql", "SELECT 1;\r\n-- ```\r\n");
    expect(page.outputPath).toBe("sql/count.sql.md");
    expect(page.slug).toBe("sql/count-sql");
    expect(page.title).toBe("count.sql");
    expect(frontmatterOf(page).tableOfContents).toBe(false);
    expect(bodyOf(page)).toBe('````sql title="count.sql"\nSELECT 1;\n-- ```\n````\n');
  });

  it("closes the fence on its own line when the file lacks a final newline", () => {
    expect(bodyOf(pageFor("run.sh", "echo hi"))).toBe('```bash title="run.sh"\necho hi\n```\n');
  });

  it("warns about invalid frontmatter", () => {
    const { warnings } = buildPages([{ sourcePath: "x.md", text: "---\n: [\n---\n# T\n" }]);
    expect(warnings).toEqual([expect.stringMatching(/^x\.md: invalid frontmatter/)]);
  });

  it("fails on slug collisions naming both source files", () => {
    const files: ContentFile[] = [
      { sourcePath: "a/count.sql", text: "" },
      { sourcePath: "a/Count-SQL.md", text: "" },
      { sourcePath: "b/Note.md", text: "" },
      { sourcePath: "b/note.md", text: "" },
    ];
    expect(() => buildPages(files)).toThrow(
      /slug "a\/count-sql" is produced by both "a\/count.sql" and "a\/Count-SQL.md"[\s\S]*slug "b\/note" is produced by both "b\/Note.md" and "b\/note.md"/,
    );
  });

  it("fails on output path collisions", () => {
    const files: ContentFile[] = [
      { sourcePath: "a/README.md", text: "" },
      { sourcePath: "a/index.md", text: "" },
    ];
    expect(() => buildPages(files)).toThrow(/generated page "a\/index.md" is produced by both/);
  });

  it("fails on names that leave no slug", () => {
    expect(() => buildPages([{ sourcePath: "!!/x.md", text: "" }])).toThrow(/!!\/x\.md: Cannot derive a slug/);
  });
});

describe("buildSidebar", () => {
  it("lists folders first, then files, case-insensitively, with the README first", () => {
    const { pages } = buildPages([
      { sourcePath: "README.md", text: "# Home" },
      { sourcePath: "zeta.md", text: "# Zeta" },
      { sourcePath: "Alpha.sql", text: "" },
      { sourcePath: "beta/README.md", text: "# Beta folder" },
      { sourcePath: "beta/b.md", text: "" },
      { sourcePath: "beta/A.md", text: "" },
      { sourcePath: "beta/Sub/x.sh", text: "" },
      { sourcePath: "Gamma/item 10.md", text: "" },
      { sourcePath: "Gamma/item 9.md", text: "" },
    ]);
    expect(buildSidebar(pages)).toEqual([
      {
        label: "beta",
        collapsed: true,
        items: [
          { label: "Beta folder", slug: "beta" },
          { label: "Sub", collapsed: true, items: [{ label: "x.sh", slug: "beta/sub/x-sh" }] },
          { label: "A", slug: "beta/a" },
          { label: "b", slug: "beta/b" },
        ],
      },
      {
        label: "Gamma",
        collapsed: true,
        items: [
          { label: "item 9", slug: "gamma/item-9" },
          { label: "item 10", slug: "gamma/item-10" },
        ],
      },
      { label: "Alpha.sql", slug: "alpha-sql" },
      { label: "Zeta", slug: "zeta" },
    ]);
  });
});

describe("normalizeBase", () => {
  it.each([
    [undefined, ""],
    ["/", ""],
    ["/repo", "/repo"],
    ["repo/", "/repo"],
  ])("%s → %j", (base, expected) => {
    expect(normalizeBase(base)).toBe(expected);
  });
});

describe("scanContent and prepareContent", () => {
  let root: string;
  let contentDir: string;
  let siteDir: string;

  beforeEach(async () => {
    root = await mkdtemp(path.join(tmpdir(), "snipcache-"));
    contentDir = path.join(root, "content");
    siteDir = path.join(root, "site");
    await mkdir(path.join(contentDir, "notes"), { recursive: true });
    await mkdir(path.join(contentDir, ".github/workflows"), { recursive: true });
    await mkdir(path.join(contentDir, ".hidden"), { recursive: true });
    await writeFile(path.join(contentDir, "README.md"), "# Home\n");
    await writeFile(path.join(contentDir, "notes/a.md"), "# A\n");
    await writeFile(path.join(contentDir, "notes/run.ps1"), "﻿Write-Host hi\n");
    await writeFile(path.join(contentDir, "notes/.secret.md"), "x");
    await writeFile(path.join(contentDir, ".github/workflows/deploy.yml"), "x");
    await writeFile(path.join(contentDir, ".hidden/y.md"), "x");
    await writeFile(path.join(contentDir, "notes/image.png"), Buffer.from([0x89, 0x50, 0x4e, 0x47, 0xff, 0xfe]));
    await writeFile(path.join(contentDir, "notes/nul.txt"), "a\0b");
  });

  afterEach(async () => {
    await rm(root, { recursive: true, force: true });
  });

  it("skips dot entries and binary files with warnings for the binaries", async () => {
    const { files, warnings } = await scanContent(contentDir);
    expect(files.map((f) => f.sourcePath).sort()).toEqual(["README.md", "notes/a.md", "notes/run.ps1"]);
    expect(files.find((f) => f.sourcePath === "notes/run.ps1")!.text).toBe("Write-Host hi\n");
    expect(warnings.sort()).toEqual([
      expect.stringMatching(/^Skipping notes\/image\.png: not valid UTF-8/),
      expect.stringMatching(/^Skipping notes\/nul\.txt: contains NUL/),
    ]);
  });

  it("writes pages, raw copies and the sidebar, replacing earlier output", async () => {
    await mkdir(path.join(siteDir, "src/content/docs"), { recursive: true });
    await writeFile(path.join(siteDir, "src/content/docs/stale.md"), "old");

    await prepareContent({ contentDir, siteDir });

    const docs = path.join(siteDir, "src/content/docs");
    await expect(readFile(path.join(docs, "stale.md"))).rejects.toThrow();
    expect(await readFile(path.join(docs, "index.md"), "utf8")).toMatch(/^---\ntitle: Home\n/);
    expect(await readFile(path.join(docs, "notes/run.ps1.md"), "utf8")).toContain('```powershell title="run.ps1"');
    expect(await readFile(path.join(siteDir, "public/raw/notes/run.ps1"), "utf8")).toBe("﻿Write-Host hi\n");
    expect(await readFile(path.join(siteDir, "public/raw/README.md"), "utf8")).toBe("# Home\n");
    const sidebar = JSON.parse(await readFile(path.join(siteDir, "src/generated/sidebar.json"), "utf8"));
    expect(sidebar).toEqual([
      {
        label: "notes",
        collapsed: true,
        items: [
          { label: "A", slug: "notes/a" },
          { label: "run.ps1", slug: "notes/run-ps1" },
        ],
      },
    ]);
  });
});
