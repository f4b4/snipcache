import type { ReactElement } from "react";
import { render } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import { AuthContext, type AuthState } from "@snipcache/auth";

export const signedIn: AuthState = {
  user: { sub: "u1", name: "Ada", email: "ada@example.org", roles: [], claims: {} },
  isLoading: false,
  isAuthenticated: true,
  serviceError: false,
  login: () => {},
  logout: () => {},
};

/**
 * Renders `element` at `url` inside a data router (needed by useBlocker), a
 * fresh QueryClient and a signed-in auth context. `/unauthorized` renders a
 * marker so redirects can be asserted.
 */
export function renderAt(element: ReactElement, url = "/") {
  const router = createMemoryRouter(
    [
      { path: "/unauthorized", element: <p>unauthorized page</p> },
      { path: "*", element },
    ],
    { initialEntries: [url] },
  );
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const result = render(
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={signedIn}>
        <RouterProvider router={router} />
      </AuthContext.Provider>
    </QueryClientProvider>,
  );
  return { ...result, router, queryClient };
}
