// How a content file path maps to the slug of its page on the site. The site
// builder writes these slugs into the generated pages and the editor builds
// its "View on site" links from them, so both must use this module.

const TRANSLITERATIONS: Record<string, string> = {
  ä: "ae",
  ö: "oe",
  ü: "ue",
  ß: "ss",
};

/** True for files rendered as Markdown pages; every other file becomes a code page. */
export function isMarkdownPath(path: string): boolean {
  return /\.md$/i.test(path);
}

/** True for a folder's `README.md`, which becomes the folder's index page. */
export function isReadmePath(path: string): boolean {
  return /^readme\.md$/i.test(basename(path));
}

/**
 * Slug for one path segment: lower-cased, umlauts transliterated, other
 * accents stripped, dots and whitespace turned into `-`, everything except
 * letters, digits, `-` and `_` dropped. Throws when nothing is left.
 */
export function slugifySegment(segment: string): string {
  const slug = segment
    .normalize("NFC")
    .toLowerCase()
    .replace(/[äöüß]/g, (c) => TRANSLITERATIONS[c]!)
    .normalize("NFD")
    .replace(/\p{M}/gu, "")
    .replace(/[.\s]+/gu, "-")
    .replace(/[^\p{L}\p{N}_-]/gu, "")
    .replace(/-{2,}/g, "-")
    .replace(/^-|-$/g, "");
  if (slug === "") {
    throw new Error(`Cannot derive a slug from "${segment}"`);
  }
  return slug;
}

/**
 * Slug of the page generated for a content file, e.g. `a/b/README.md` → `a/b`,
 * `a/b/note.md` → `a/b/note`, `a/b/count.sql` → `a/b/count-sql`. The root
 * `README.md` maps to the empty string (the home page).
 */
export function pageSlug(sourcePath: string): string {
  const segments = sourcePath.split("/").filter((s) => s !== "");
  const fileName = segments.pop();
  if (fileName === undefined) {
    throw new Error("Empty content path");
  }
  const parts = segments.map(slugifySegment);
  if (!isReadmePath(fileName)) {
    const stem = isMarkdownPath(fileName) ? fileName.slice(0, -".md".length) : fileName;
    parts.push(slugifySegment(stem));
  }
  return parts.join("/");
}

/** Absolute URL of a content file's page, given the site's base URL. */
export function pageUrl(siteBaseUrl: string, sourcePath: string): string {
  const slug = pageSlug(sourcePath);
  const path = slug === "" ? "" : slug.split("/").map(encodeURIComponent).join("/") + "/";
  return `${siteBaseUrl.replace(/\/+$/, "")}/${path}`;
}

function basename(path: string): string {
  return path.slice(path.lastIndexOf("/") + 1);
}
