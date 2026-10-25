import type { ReactNode } from "react";

/** Frame for the pages shown without a session: sign-in, signed out, no access. */
export function PublicShell({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="flex h-full flex-col">
      <header className="flex items-center border-b border-border px-4 py-2.5">
        <span className="font-semibold">SnipCache Editor</span>
      </header>
      <main className="mx-auto flex max-w-md flex-1 flex-col items-center justify-center gap-5 px-5 text-center">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        {children}
      </main>
    </div>
  );
}
