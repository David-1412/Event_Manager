import { Button } from "@/components/ui/button";

/** Shared failure state for the Account cards: what failed, and a way to retry. */
export function CardError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div role="alert" className="flex flex-col items-start gap-3">
      <p className="text-meta text-fg-muted">{message}</p>
      <Button variant="secondary" size="sm" onClick={onRetry}>
        Try again
      </Button>
    </div>
  );
}
