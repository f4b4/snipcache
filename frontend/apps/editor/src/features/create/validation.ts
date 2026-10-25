import { pageSlug } from "@snipcache/content-rules";
import type { NodeType } from "../../api/generated/model";
import { errorStatus } from "../../api/mutator";
import { newPathError } from "../../lib/paths";

/** Every path in the repository with its kind. */
export type ExistingPaths = ReadonlyMap<string, NodeType>;

/**
 * Checks a path the editor is about to create: the backend's name rules, an
 * existing entry with that path, and an existing file whose page would get the
 * same slug (which would fail the site build). Returns a user-facing error.
 */
export function createPathError(path: string, existing: ExistingPaths): string | undefined {
  const nameError = newPathError(path);
  if (nameError) return nameError;
  if (existing.has(path)) return `${path} already exists.`;

  let slug: string;
  try {
    slug = pageSlug(path);
  } catch {
    return "Every name needs at least one letter or digit.";
  }
  for (const [other, type] of existing) {
    if (type !== "file") continue; // folders have no page of their own
    let otherSlug: string;
    try {
      otherSlug = pageSlug(other);
    } catch {
      continue;
    }
    if (otherSlug === slug) return `The page would get the same address as ${other}.`;
  }
  return undefined;
}

/** Message for a failed create: 409 means someone created the path on GitHub meanwhile. */
export function createFailedMessage(error: unknown, path: string): string {
  if (errorStatus(error) === 409) return `${path} already exists on GitHub.`;
  return error instanceof Error ? error.message : String(error);
}
