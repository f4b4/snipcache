// Content repository paths as the editor handles them: relative, "/"-separated,
// unencoded. They are encoded per segment only when they go into a URL.

/** Encodes each segment for use in a URL path, keeping the "/" separators. */
export function encodePath(path: string): string {
  return path.split("/").map(encodeURIComponent).join("/");
}

/** The editor route of a file, e.g. `/edit/SQL/count.sql`. */
export function editRoute(path: string): string {
  return `/edit/${encodePath(path)}`;
}

/**
 * The file path of an editor route (the inverse of {@link editRoute}), or
 * undefined for any other route. Also accepts the unencoded links a browser
 * may keep after following an edit link from the site.
 */
export function pathFromRoute(pathname: string): string | undefined {
  const match = /^\/edit\/(.+)$/.exec(pathname);
  if (!match) return undefined;
  const path = match[1]!
    .split("/")
    .filter((s) => s !== "")
    .map((s) => {
      try {
        return decodeURIComponent(s);
      } catch {
        return s;
      }
    })
    .join("/");
  return path === "" ? undefined : path;
}

/** The last segment of a path. */
export function baseName(path: string): string {
  return path.slice(path.lastIndexOf("/") + 1);
}

/** The folder of a path; "" for the repository root. */
export function parentPath(path: string): string {
  const i = path.lastIndexOf("/");
  return i < 0 ? "" : path.slice(0, i);
}

/** Joins a folder ("" for the root) and a name. */
export function joinPath(folder: string, name: string): string {
  return folder === "" ? name : `${folder}/${name}`;
}

/** Normalizes user input for a folder: trims, "\" → "/", no outer slashes. */
export function normalizeFolder(input: string): string {
  return input
    .normalize("NFC")
    .replace(/\\/g, "/")
    .trim()
    .replace(/^\/+|\/+$/g, "");
}

// Same as the backend's ContentPath.SegmentPattern (^[\w .\-()]+$); .NET's \w
// covers Unicode letters, digits and connector punctuation.
const NEW_NAME = /^[\p{L}\p{Mn}\p{Nd}\p{Pc} .\-()]+$/u;

/**
 * Checks a path the editor is about to create, with the backend's rules for
 * new names (ContentPath.TryNormalize with newName): no empty segments, no
 * leading dot, no surrounding whitespace, only letters, digits, spaces and
 * `_ . - ( )`. Returns a user-facing error, or undefined when the path is fine.
 */
export function newPathError(path: string): string | undefined {
  if (path === "") return "The name is required.";
  if (path.length > 400) return "The path must not be longer than 400 characters.";
  for (const segment of path.split("/")) {
    if (segment === "") return "The path must not contain empty segments.";
    if (segment.startsWith(".")) return `'${segment}': names must not start with a dot.`;
    if (segment !== segment.trim()) {
      return `'${segment}': names must not start or end with whitespace.`;
    }
    if (!NEW_NAME.test(segment)) {
      return `'${segment}': only letters, digits, spaces and _ . - ( ) are allowed.`;
    }
  }
  return undefined;
}
