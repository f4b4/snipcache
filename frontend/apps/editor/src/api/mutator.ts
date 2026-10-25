import { apiFetch } from "@snipcache/auth";

/** The RFC 9457 problem body the backend returns for every error. */
export interface Problem {
  title?: string | null;
  detail?: string | null;
  status?: number | null;
  /** Validation errors by field (400), e.g. `{ path: ["…"] }`. */
  errors?: Record<string, string[]>;
}

/**
 * A non-2xx response. Carries the status so callers can branch on it (409
 * conflict, 404 missing file) and the problem body for a readable message.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: Problem | null;

  constructor(status: number, problem: Problem | null, fallback: string) {
    super(problemMessage(problem) ?? fallback);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }
}

function problemMessage(problem: Problem | null): string | undefined {
  if (!problem) return undefined;
  const fieldErrors = Object.values(problem.errors ?? {}).flat();
  if (fieldErrors.length > 0) return fieldErrors.join(" ");
  return problem.detail ?? problem.title ?? undefined;
}

/** The status of an error thrown by a generated hook, if it came from the API. */
export function errorStatus(error: unknown): number | undefined {
  return error instanceof ApiError ? error.status : undefined;
}

/**
 * Orval fetch-mutator. Every generated TanStack Query hook calls through here,
 * so all backend traffic inherits @snipcache/auth's BFF behavior: credentials:include
 * (session cookie), the X-CSRF header on mutating verbs, and the 401 → login
 * redirect. No component or generated file ever constructs its own fetch.
 *
 * Orval (httpClient: "fetch") invokes this as `apiFetchMutator<T>(url, requestInit)`
 * and, with `includeHttpResponseReturnType: false`, expects the parsed body back.
 */
export const apiFetchMutator = async <T>(
  url: string,
  options: RequestInit = {},
): Promise<T> => {
  // apiFetch already turns a 401 into a login redirect (and throws), and applies
  // cookies + X-CSRF; other non-2xx become an ApiError with the problem body.
  const res = await apiFetch(url, options);
  if (!res.ok) {
    const problem = (await res.json().catch(() => null)) as Problem | null;
    throw new ApiError(
      res.status,
      problem,
      `${options.method ?? "GET"} ${url} → ${res.status}`,
    );
  }
  return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
};
