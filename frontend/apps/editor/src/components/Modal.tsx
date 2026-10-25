import { useEffect, useId, useRef, type ReactNode } from "react";

interface ModalProps {
  title: string;
  /** Called on Escape and on a click on the backdrop. */
  onClose: () => void;
  children: ReactNode;
}

/**
 * A centered dialog over a dimmed backdrop. Focuses its first form control on
 * open so dialogs can be typed into right away.
 */
export function Modal({ title, onClose, children }: ModalProps) {
  const titleId = useId();
  const panel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    panel.current?.querySelector<HTMLElement>("input, textarea, button")?.focus();
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="w-full max-w-md rounded-lg border border-border bg-surface p-5 shadow-xl"
      >
        <h2 id={titleId} className="mb-4 text-base font-semibold">
          {title}
        </h2>
        {children}
      </div>
    </div>
  );
}
