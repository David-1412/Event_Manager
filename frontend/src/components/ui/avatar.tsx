import { cn } from "@/lib/cn";
import { initialsOf } from "@/lib/cn";

const sizes = { sm: "h-7 w-7 text-micro", md: "h-9 w-9 text-meta", lg: "h-14 w-14 text-body" } as const;

/** Initials on surface-2 fallback — never a broken-image icon (spec §7). */
export function Avatar({
  name,
  src,
  size = "md",
  className,
}: {
  name: string;
  src?: string | null;
  size?: keyof typeof sizes;
  className?: string;
}) {
  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-surface-2 font-medium text-fg-muted",
        sizes[size],
        className,
      )}
      title={name}
    >
      {src ? (
        // eslint-disable-next-line @next/next/no-img-element -- avatars come from arbitrary URLs (Google/Firebase) until the asset host exists
        <img src={src} alt="" className="h-full w-full object-cover" />
      ) : (
        <span aria-hidden>{initialsOf(name)}</span>
      )}
      <span className="sr-only">{name}</span>
    </span>
  );
}

/** Overlapping participant stack: max 4 then `+3` (spec §7). */
export function AvatarStack({
  people,
  hiddenCount = 0,
  size = "sm",
}: {
  people: { id: string; displayName: string; avatarUrl: string | null }[];
  hiddenCount?: number;
  size?: keyof typeof sizes;
}) {
  const shown = people.slice(0, 4);
  const remaining = hiddenCount || Math.max(0, people.length - shown.length);
  return (
    <span className="flex items-center -space-x-2">
      {shown.map((p) => (
        <Avatar key={p.id} name={p.displayName} src={p.avatarUrl} size={size} className="ring-2 ring-surface" />
      ))}
      {remaining > 0 && (
        <span
          className={cn(
            "inline-flex items-center justify-center rounded-full bg-surface-2 font-medium text-fg-muted ring-2 ring-surface",
            sizes[size],
          )}
        >+{remaining}</span>
      )}
    </span>
  );
}
