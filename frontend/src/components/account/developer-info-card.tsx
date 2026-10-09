import { Card } from "@/components/ui/card";
import type { AuthUser } from "@/lib/auth/types";

/**
 * Diagnostics for whoever is debugging sign-in. A native <details>, so it is
 * collapsed by default, keyboard-accessible, and renders none of this until opened.
 */
export function DeveloperInfoCard({
  user,
  hasToken,
  apiTrusted,
}: {
  user: AuthUser;
  hasToken: boolean;
  apiTrusted: boolean;
}) {
  const rows: [string, string][] = [
    ["Firebase UID", user.uid],
    ["Internal account ID", user.id ?? "Not issued"],
    ["Session", hasToken ? "Token present" : "No token"],
    ["API verification", apiTrusted ? "Verified" : "Not verified"],
  ];

  return (
    <Card className="p-4 sm:p-6">
      <details className="group">
        <summary className="cursor-pointer list-none text-h3 text-fg">
          <span className="inline-flex items-center gap-2">
            Developer information
            <span aria-hidden className="text-meta text-fg-muted transition-transform group-open:rotate-180">
              ▼
            </span>
          </span>
        </summary>
        <dl className="mt-4 divide-y divide-border">
          {rows.map(([label, value]) => (
            <div key={label} className="flex flex-wrap items-baseline justify-between gap-2 py-2">
              <dt className="text-meta font-medium text-fg">{label}</dt>
              <dd className="break-all font-mono text-micro text-fg-muted">{value}</dd>
            </div>
          ))}
        </dl>
      </details>
    </Card>
  );
}
