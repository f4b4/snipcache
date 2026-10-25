import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router-dom";
import { AuthProvider, UnauthenticatedError } from "@snipcache/auth";
import { errorStatus } from "./api/mutator";
import { router } from "./app/router";
import "./index.css";

// Shared by every Orval-generated hook; generation is configured in
// orval.config.ts and run via `pnpm --filter editor generate:api`.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Retry network and server errors only; a 4xx will not change on retry,
      // and a 401 has already started the sign-in redirect.
      retry: (failureCount, error) => {
        if (failureCount >= 2 || error instanceof UnauthenticatedError) return false;
        const status = errorStatus(error);
        return status === undefined || status >= 500;
      },
    },
  },
});

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error("Root element #root not found");
}

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
