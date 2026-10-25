import { useId, type InputHTMLAttributes } from "react";

interface FieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "id"> {
  label: string;
  hint?: string;
}

/** A labelled text input for the dialogs. */
export function Field({ label, hint, className = "", ...props }: FieldProps) {
  const id = useId();
  return (
    <div className="mb-3 flex flex-col gap-1">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>
      <input
        id={id}
        type="text"
        autoComplete="off"
        spellCheck={false}
        className={`rounded-md border border-border bg-surface px-2.5 py-1.5 text-sm outline-none focus:border-brand ${className}`}
        {...props}
      />
      {hint && <span className="text-xs text-content-muted">{hint}</span>}
    </div>
  );
}
