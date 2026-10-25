// Smoke test for a finished `pnpm build`: the Pagefind index, one HTML page
// and one raw copy per content file, and links that respect SNIPCACHE_BASE
// and SNIPCACHE_EDITOR_URL. Reads the same environment as the build.

import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { buildPages, normalizeBase, scanContent } from "./prepare-content.ts";

const siteDir = fileURLToPath(new URL("..", import.meta.url));
const distDir = path.join(siteDir, "dist");
const env = process.env;
const contentDir = path.resolve(env.SNIPCACHE_CONTENT_DIR || path.join(siteDir, "sample-content"));
const base = normalizeBase(env.SNIPCACHE_BASE);
const editorUrl = env.SNIPCACHE_EDITOR_URL?.replace(/\/+$/, "");

const failures: string[] = [];
const check = (ok: boolean, message: string) => {
  if (!ok) failures.push(message);
};

check(existsSync(path.join(distDir, "pagefind/pagefind.js")), "dist/pagefind/pagefind.js is missing");

const { files } = await scanContent(contentDir);
const { pages } = buildPages(files, { base, editorUrl });
for (const file of files) {
  const raw = path.join(distDir, "raw", file.sourcePath);
  check(existsSync(raw), `raw copy of ${file.sourcePath} is missing`);
}

for (const page of pages) {
  const html = path.join(distDir, page.slug, "index.html");
  if (!existsSync(html)) {
    check(false, `page for ${page.sourcePath || "the home page"} is missing (${path.relative(siteDir, html)})`);
    continue;
  }
  if (page.sourcePath === "") continue;
  const text = readFileSync(html, "utf8");
  const rawHref = `href="${base}/raw/${page.sourcePath.split("/").map(encodeURIComponent).join("/")}"`;
  check(text.includes(rawHref), `${page.slug}: no raw link ${rawHref}`);
  check(text.includes(`href="${base}/_astro/`), `${page.slug}: assets are not served under base "${base}/"`);
  if (editorUrl) {
    check(text.includes(`href="${editorUrl}/edit/`), `${page.slug}: no edit link to ${editorUrl}`);
  }
  if (page.kind === "file") {
    check(text.includes('class="expressive-code"'), `${page.slug}: code page has no code block`);
  }
}

if (failures.length > 0) {
  console.error(`check-build: ${failures.length} problem(s):\n  ${failures.join("\n  ")}`);
  process.exit(1);
}
console.log(`check-build: ${pages.length} pages and ${files.length} raw files OK (base "${base || "/"}")`);
