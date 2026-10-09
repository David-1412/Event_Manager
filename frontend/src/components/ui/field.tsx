"use client";

import {
  forwardRef,
  useId,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from "react";
import { cn } from "@/lib/cn";

/**
 * Field contract (spec §7): label above the control, helper or error underneath,
 * `aria-invalid` + `aria-describedby` wired when an error exists. Native inputs
 * are styled rather than replaced, so mobile pickers stay native.
 */

export interface FieldProps {
  label: string;
  /** Rendered as `label` element text; keep it a plain string for a11y. */
  hint?: string;
  error?: string;
  required?: boolean;
  optional?: boolean;
  className?: string;
  /** A "check this" marker from an import: shown under the control, clears itself when
   *  the field is edited, or when the user says it looks right. */
  flag?: { text: string; onDismiss: () => void };
  children: (ids: { id: string; describedBy?: string; invalid: boolean }) => ReactNode;
}

export function Field({
  label,
  hint,
  error,
  required,
  optional,
  className,
  flag,
  children,
}: FieldProps) {
  const autoId = useId();
  const id = autoId;
  const errorId = `${autoId}-error`;
  const hintId = `${autoId}-hint`;
  const describedBy = error ? errorId : hint ? hintId : undefined;

  return (
    <div className={cn("flex flex-col gap-1", className)}>
      <label htmlFor={id} className="text-meta font-medium text-fg">
        {label}
        {required && <span className="text-danger"> *</span>}
        {optional && <span className="text-fg-muted"> (optional)</span>}
      </label>
      {children({ id, describedBy, invalid: Boolean(error) })}
      {error ? (
        <p id={errorId} className="text-meta text-danger">
          {error}
        </p>
      ) : hint ? (
        <p id={hintId} className="text-meta text-fg-muted">
          {hint}
        </p>
      ) : null}
      {flag && (
        <p role="status" className="flex flex-wrap items-center gap-x-2 text-meta text-warn">
          <span aria-hidden>⚠</span>
          <span>{flag.text}</span>
          <button type="button" onClick={flag.onDismiss} className="underline underline-offset-2">
            Looks right
          </button>
        </p>
      )}
    </div>
  );
}

const controlBase =
  "h-11 w-full rounded-md border border-border bg-surface px-3 text-body text-fg " +
  "placeholder:text-fg-muted disabled:bg-surface-2 disabled:text-fg-muted " +
  "aria-[invalid=true]:border-danger";

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(
  function Input({ className, ...rest }, ref) {
    return <input ref={ref} className={cn(controlBase, "read-only:bg-surface-2", className)} {...rest} />;
  },
);

export const Textarea = forwardRef<
  HTMLTextAreaElement,
  TextareaHTMLAttributes<HTMLTextAreaElement>
>(function Textarea({ className, rows = 4, ...rest }, ref) {
  return (
    <textarea
      ref={ref}
      rows={rows}
      className={cn(controlBase, "h-auto min-h-24 py-2 leading-relaxed", className)}
      {...rest}
    />
  );
});

/** Native select, `appearance-none` + caret (spec §7). */
export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(
  function Select({ className, children, ...rest }, ref) {
    return (
      <div className="relative">
        <select
          ref={ref}
          className={cn(controlBase, "cursor-pointer appearance-none pr-9", className)}
          {...rest}
        >
          {children}
        </select>
        <svg
          viewBox="0 0 16 16"
          width="14"
          height="14"
          fill="none"
          aria-hidden="true"
          className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-fg-muted"
        >
          <path
            d="M4 6.5 8 10.5 12 6.5"
            stroke="currentColor"
            strokeWidth="1.8"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      </div>
    );
  },
);
