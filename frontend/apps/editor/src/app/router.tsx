import { createBrowserRouter, Navigate } from "react-router-dom";
import { ProtectedRoute } from "@snipcache/auth";
import { EditorPage } from "../pages/EditorPage";
import { HomePage } from "../pages/HomePage";
import { LoggedOutPage } from "../pages/LoggedOutPage";
import { UnauthorizedPage } from "../pages/UnauthorizedPage";

export const router = createBrowserRouter([
  // Public routes: no session required.
  { path: "/home", element: <HomePage /> },
  { path: "/logged-out", element: <LoggedOutPage /> },
  { path: "/unauthorized", element: <UnauthorizedPage /> },

  // The editor. One layout route for "/" and "/edit/*" so the tree keeps its
  // state while files are opened; EditorPage reads the file from the URL.
  {
    path: "/",
    element: (
      <ProtectedRoute redirectTo="/home">
        <EditorPage />
      </ProtectedRoute>
    ),
    children: [
      { index: true, element: null },
      { path: "edit/*", element: null },
    ],
  },

  { path: "*", element: <Navigate to="/" replace /> },
]);
