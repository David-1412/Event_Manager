import { cn } from "@/lib/cn";

/** Hairline card: `shadow-0` is the resting state; elevation is rare (spec §4). */
export function Card({
  className,
  tone = "default",
  ...rest
}: React.HTMLAttributes<HTMLDivElement> & {
  tone?: "default" | "mine" | "cancelled";
}) {
  return (
    <div
      className={cn(
        "rounded-md border border-border bg-surface",
        tone === "mine" && "border-l-[3px] border-l-brand-600",
        tone === "cancelled" && "bg-surface-2 text-fg-muted",
        className,
      )}
      {...rest}
    />
  );
}

/** Sticky bars use `shadow-raise`; popovers/sheets/modals use `shadow-float`. */
export function Floating({ className, ...rest }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "rounded-md border border-border bg-surface shadow-float",
        className,
      )}
      {...rest}
    />
  );
}
