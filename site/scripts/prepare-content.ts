// Turns a content repository's file tree into Starlight pages (see section 4.1
// of doc/snipcache-architecture-and-plan.md). Runs before `astro dev` and
// `astro build`; everything it writes is gitignored.
//
//   SNIPCACHE_CONTENT_DIR  content tree to read (default: site/sample-content)
//   SNIPCACHE_BASE         site base path, used for the raw file links
//   SNIPCACHE_EDITOR_URL   editor origin; when set, pages get edit links
//   SNIPCACHE_TITLE        site title, used for a generated home page

import { cp, mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { isMarkdownPath, isReadmePath, pageSlug } from "@snipcache/content-rules";
import { parse as parseYaml, stringify as stringifyYaml } from "yaml";

export interface PrepareOptions {
  contentDir: string;
  /** The `site/` directory; outputs go to its `src/content/docs`, `public/raw` and `src/generated`. */
  siteDir: string;
  base?: string;
  editorUrl?: string;
  siteTitle?: string;
}

export interface ContentFile {
  /** Path relative to the content root, with `/` separators. */
  sourcePath: string;
  text: string;
}

export type PageKind = "markdown" | "file";

export interface Page {
  sourcePath: string;
  /** Path of the generated page relative to `src/content/docs`. */
  outputPath: string;
  slug: string;
  title: string;
  kind: PageKind;
  language: string;
  /** Complete file contents of the generated page, frontmatter included. */
  document: string;
}

export type SidebarItem =
  | { label: string; slug: string }
  | { label: string; collapsed: boolean; items: SidebarItem[] };

const LANGUAGES: Record<string, string> = {
  ps1: "powershell",
  psm1: "powershell",
  sh: "bash",
  bash: "bash",
  zsh: "bash",
  bat: "batch",
  cmd: "batch",
  sql: "sql",
  json: "json",
  yaml: "yaml",
  yml: "yaml",
  xml: "xml",
  csproj: "xml",
  toml: "toml",
  ini: "ini",
  py: "python",
  js: "javascript",
  ts: "typescript",
  cs: "csharp",
};

/** Highlighting language for a non-Markdown file, from its name. */
export function languageFor(fileName: string): string {
  if (fileName.toLowerCase() === "dockerfile") return "dockerfile";
  const dot = fileName.lastIndexOf(".");
  if (dot <= 0) return "text";
  return LANGUAGES[fileName.slice(dot + 1).toLowerCase()] ?? "text";
}

/** A backtick fence longer than any backtick run inside `content`. */
export function fenceFor(content: string): string {
  const longest = Math.max(0, ...[...content.matchAll(/`+/g)].map((m) => m[0].length));
  return "`".repeat(Math.max(3, longest + 1));
}

/** Splits optional YAML frontmatter from a Markdown file. */
export function splitFrontmatter(text: string): {
  frontmatter: Record<string, unknown>;
  body: string;
  error?: string;
} {
  const match = /^---[ \t]*\r?\n(?:([\s\S]*?)\r?\n)?---[ \t]*(?:\r?\n|$)/.exec(text);
  if (!match) return { frontmatter: {}, body: text };
  const body = text.slice(match[0].length);
  try {
    const data: unknown = parseYaml(match[1] ?? "");
    if (data == null) return { frontmatter: {}, body };
    if (typeof data !== "object" || Array.isArray(data)) {
      return { frontmatter: {}, body, error: "frontmatter is not a YAML mapping" };
    }
    return { frontmatter: data as Record<string, unknown>, body };
  } catch (e) {
    return { frontmatter: {}, body, error: `invalid frontmatter: ${(e as Error).message}` };
  }
}

/**
 * Title of a Markdown page: frontmatter `title`, else the first `# H1`
 * outside code fences (removed from the body), else the file name without
 * `.md`. The returned body has leading blank lines trimmed.
 */
export function resolveMarkdownTitle(
  sourcePath: string,
  frontmatter: Record<string, unknown>,
  body: string,
): { title: string; body: string } {
  const fmTitle = frontmatter.title;
  if ((typeof fmTitle === "string" && fmTitle.trim() !== "") || typeof fmTitle === "number") {
    return { title: String(fmTitle).trim(), body: trimLeadingBlankLines(body) };
  }

  const lines = body.split("\n");
  let fence: { char: string; length: number } | undefined;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]!.replace(/\r$/, "");
    const fenceMatch = /^ {0,3}(`{3,}|~{3,})/.exec(line);
    if (fence) {
      if (fenceMatch && fenceMatch[1]![0] === fence.char && fenceMatch[1]!.length >= fence.length) {
        fence = undefined;
      }
      continue;
    }
    if (fenceMatch) {
      fence = { char: fenceMatch[1]![0]!, length: fenceMatch[1]!.length };
      continue;
    }
    const h1 = /^ {0,3}#[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$/.exec(line);
    if (h1 && h1[1]!.trim() !== "") {
      lines.splice(i, 1);
      return { title: h1[1]!.trim(), body: trimLeadingBlankLines(lines.join("\n")) };
    }
  }

  const fileName = sourcePath.slice(sourcePath.lastIndexOf("/") + 1);
  const title = isReadmePath(sourcePath) ? folderName(sourcePath) : fileName.replace(/\.md$/i, "");
  return { title, body: trimLeadingBlankLines(body) };
}

/** Reads every text file under `contentDir`, skipping dot entries, symlinks and binary files. */
export async function scanContent(contentDir: string): Promise<{ files: ContentFile[]; warnings: string[] }> {
  const files: ContentFile[] = [];
  const warnings: string[] = [];
  const decoder = new TextDecoder("utf-8", { fatal: true });

  async function walk(relDir: string): Promise<void> {
    const entries = await readdir(path.join(contentDir, relDir), { withFileTypes: true });
    for (const entry of entries) {
      if (entry.name.startsWith(".")) continue;
      const rel = relDir === "" ? entry.name : `${relDir}/${entry.name}`;
      if (entry.isDirectory()) {
        await walk(rel);
      } else if (entry.isFile()) {
        const bytes = await readFile(path.join(contentDir, rel));
        let text: string;
        try {
          text = decoder.decode(bytes);
        } catch {
          warnings.push(`Skipping ${rel}: not valid UTF-8 (binary file?)`);
          continue;
        }
        if (text.includes("\0")) {
          warnings.push(`Skipping ${rel}: contains NUL bytes (binary file?)`);
          continue;
        }
        files.push({ sourcePath: rel, text: text.replace(/^﻿/, "") });
      } else {
        warnings.push(`Skipping ${rel}: not a regular file`);
      }
    }
  }

  await walk("");
  return { files, warnings };
}

/** Builds the generated pages. Throws when two files map to the same slug or output path. */
export function buildPages(
  files: ContentFile[],
  options: Pick<PrepareOptions, "base" | "editorUrl" | "siteTitle"> = {},
): { pages: Page[]; warnings: string[] } {
  const base = normalizeBase(options.base);
  const editorUrl = options.editorUrl?.replace(/\/+$/, "") || undefined;
  const pages: Page[] = [];
  const warnings: string[] = [];
  const errors: string[] = [];

  for (const file of files) {
    let slug: string;
    try {
      slug = pageSlug(file.sourcePath);
    } catch (e) {
      errors.push(`${file.sourcePath}: ${(e as Error).message}`);
      continue;
    }
    const encodedPath = file.sourcePath.split("/").map(encodeURIComponent).join("/");
    const generated: Record<string, unknown> = {};
    let title: string;
    let kind: PageKind;
    let language: string;
    let content: string;

    if (isMarkdownPath(file.sourcePath)) {
      const split = splitFrontmatter(file.text);
      if (split.error) warnings.push(`${file.sourcePath}: ${split.error}; ignoring it`);
      const resolved = resolveMarkdownTitle(file.sourcePath, split.frontmatter, split.body);
      title = resolved.title;
      kind = "markdown";
      language = "markdown";
      content = resolved.body;
      if (typeof split.frontmatter.description === "string") {
        generated.description = split.frontmatter.description;
      }
    } else {
      const fileName = file.sourcePath.slice(file.sourcePath.lastIndexOf("/") + 1);
      const text = file.text.replace(/\r\n/g, "\n");
      const fence = fenceFor(text);
      title = fileName;
      kind = "file";
      language = languageFor(fileName);
      content =
        `${fence}${language} title="${fileName.replace(/"/g, "")}"\n` +
        `${text}${text.endsWith("\n") || text === "" ? "" : "\n"}${fence}\n`;
      generated.tableOfContents = false;
    }

    const frontmatter: Record<string, unknown> = { title, ...generated };
    if (slug !== "") frontmatter.slug = slug;
    if (editorUrl) frontmatter.editUrl = `${editorUrl}/edit/${encodedPath}`;
    frontmatter.snipcache = {
      sourcePath: file.sourcePath,
      rawUrl: `${base}/raw/${encodedPath}`,
      kind,
      language,
    };

    pages.push({
      sourcePath: file.sourcePath,
      outputPath: outputPathFor(file.sourcePath),
      slug,
      title,
      kind,
      language,
      document: `---\n${stringifyYaml(frontmatter)}---\n\n${content}`,
    });
  }

  errors.push(...findCollisions(pages, (p) => p.slug, "slug"));
  errors.push(...findCollisions(pages, (p) => p.outputPath.toLowerCase(), "generated page"));
  if (errors.length > 0) {
    throw new Error(`Cannot build the site:\n  ${errors.join("\n  ")}`);
  }

  if (!pages.some((p) => p.slug === "")) {
    const siteTitle = options.siteTitle || "SnipCache";
    pages.push({
      sourcePath: "",
      outputPath: "index.md",
      slug: "",
      title: siteTitle,
      kind: "markdown",
      language: "markdown",
      document:
        `---\n${stringifyYaml({ title: siteTitle })}---\n\n` +
        "Browse the snippets in the sidebar or use the search.\n",
    });
  }

  return { pages, warnings };
}

/**
 * Sidebar mirroring the folder tree: folders first, then files, both sorted
 * case-insensitively; a folder's README page is its group's first entry. The
 * home page is not listed (the site title links to it).
 */
export function buildSidebar(pages: Page[]): SidebarItem[] {
  interface Folder {
    name: string;
    index?: Page;
    folders: Map<string, Folder>;
    files: Page[];
  }
  const root: Folder = { name: "", folders: new Map(), files: [] };

  for (const page of pages) {
    if (page.slug === "") continue;
    const segments = page.sourcePath.split("/");
    const fileName = segments.pop()!;
    let folder = root;
    for (const segment of segments) {
      let child = folder.folders.get(segment);
      if (!child) {
        child = { name: segment, folders: new Map(), files: [] };
        folder.folders.set(segment, child);
      }
      folder = child;
    }
    if (isReadmePath(fileName)) folder.index = page;
    else folder.files.push(page);
  }

  const collator = new Intl.Collator("en", { sensitivity: "base", numeric: true });
  const fileName = (p: Page) => p.sourcePath.slice(p.sourcePath.lastIndexOf("/") + 1);

  function items(folder: Folder): SidebarItem[] {
    const result: SidebarItem[] = [];
    if (folder.index) result.push({ label: folder.index.title, slug: folder.index.slug });
    const folders = [...folder.folders.values()].sort((a, b) => collator.compare(a.name, b.name));
    for (const child of folders) {
      result.push({ label: child.name, collapsed: true, items: items(child) });
    }
    const files = [...folder.files].sort((a, b) => collator.compare(fileName(a), fileName(b)));
    for (const page of files) result.push({ label: page.title, slug: page.slug });
    return result;
  }

  return items(root);
}

/** Runs the whole step: scan, build, and write pages, raw copies and the sidebar. */
export async function prepareContent(options: PrepareOptions): Promise<{ pages: Page[]; warnings: string[] }> {
  const scan = await scanContent(options.contentDir);
  const built = buildPages(scan.files, options);

  const docsDir = path.join(options.siteDir, "src/content/docs");
  const rawDir = path.join(options.siteDir, "public/raw");
  const generatedDir = path.join(options.siteDir, "src/generated");
  for (const dir of [docsDir, rawDir, generatedDir]) {
    await rm(dir, { recursive: true, force: true });
    await mkdir(dir, { recursive: true });
  }

  for (const page of built.pages) {
    const target = path.join(docsDir, page.outputPath);
    await mkdir(path.dirname(target), { recursive: true });
    await writeFile(target, page.document);
  }
  for (const file of scan.files) {
    const target = path.join(rawDir, file.sourcePath);
    await mkdir(path.dirname(target), { recursive: true });
    await cp(path.join(options.contentDir, file.sourcePath), target);
  }
  await writeFile(
    path.join(generatedDir, "sidebar.json"),
    JSON.stringify(buildSidebar(built.pages), null, 2) + "\n",
  );

  return { pages: built.pages, warnings: [...scan.warnings, ...built.warnings] };
}

/** `a/b/README.md` → `a/b/index.md`, `a/b/x.md` → itself, `a/b/x.sql` → `a/b/x.sql.md`. */
function outputPathFor(sourcePath: string): string {
  if (isReadmePath(sourcePath)) {
    const dir = sourcePath.slice(0, sourcePath.lastIndexOf("/") + 1);
    return `${dir}index.md`;
  }
  return isMarkdownPath(sourcePath) ? sourcePath : `${sourcePath}.md`;
}

function findCollisions(pages: Page[], key: (p: Page) => string, what: string): string[] {
  const seen = new Map<string, string>();
  const errors: string[] = [];
  for (const page of pages) {
    const k = key(page);
    const other = seen.get(k);
    if (other !== undefined) {
      errors.push(`${what} "${k}" is produced by both "${other}" and "${page.sourcePath}"`);
    } else {
      seen.set(k, page.sourcePath);
    }
  }
  return errors;
}

/** `/` or empty → ``, `docs/` → `/docs`. */
export function normalizeBase(base: string | undefined): string {
  const trimmed = (base ?? "").replace(/^\/+|\/+$/g, "");
  return trimmed === "" ? "" : `/${trimmed}`;
}

function folderName(sourcePath: string): string {
  const segments = sourcePath.split("/");
  return segments.length > 1 ? segments[segments.length - 2]! : "Home";
}

function trimLeadingBlankLines(text: string): string {
  return text.replace(/^(?:[ \t]*\r?\n)+/, "");
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const siteDir = fileURLToPath(new URL("..", import.meta.url));
  const env = process.env;
  const contentDir = path.resolve(env.SNIPCACHE_CONTENT_DIR || path.join(siteDir, "sample-content"));
  const warn = env.GITHUB_ACTIONS ? "::warning::" : "warning: ";
  try {
    const result = await prepareContent({
      contentDir,
      siteDir,
      base: env.SNIPCACHE_BASE,
      editorUrl: env.SNIPCACHE_EDITOR_URL,
      siteTitle: env.SNIPCACHE_TITLE,
    });
    for (const warning of result.warnings) console.warn(`${warn}${warning}`);
    console.log(`prepare-content: ${result.pages.length} pages from ${contentDir}`);
  } catch (e) {
    console.error((e as Error).message);
    process.exit(1);
  }
}
