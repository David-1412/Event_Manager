"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { useAuth } from "@/lib/auth/auth-provider";

const memberSinceFormat = new Intl.DateTimeFormat("en-AU", { month: "long", year: "numeric" });

export function AccountCard({
  memberSince,
  isLoading,
}: {
  /** ISO timestamp, or null/undefined when unknown (the row is then hidden). */
  memberSince: string | null | undefined;
  isLoading: boolean;
}) {
  const router = useRouter();
  const { logout } = useAuth();
  const [signingOut, setSigningOut] = useState(false);

  async function onSignOut() {
    setSigningOut(true);
    await logout();
    // Home rather than staying on a page whose whole subject is gone.
    router.replace("/");
  }

  return (
    <Card className="p-4 sm:p-6">
      <h2 className="text-h3 text-fg">Account</h2>
      {(isLoading || memberSince) && (
        <div className="mt-4 flex items-baseline justify-between gap-2">
          <span className="text-meta text-fg-muted">Member since</span>
          {isLoading ? (
            <SkeletonBlock className="h-4 w-24" />
          ) : (
            <span className="text-body text-fg">{memberSinceFormat.format(new Date(memberSince!))}</span>
          )}
        </div>
      )}
      <div className="mt-4">
        <Button variant="danger" loading={signingOut} onClick={() => void onSignOut()}>
          Sign out
        </Button>
      </div>
    </Card>
  );
}
