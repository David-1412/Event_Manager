"use client";

import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { useProfile } from "@/features/account/use-account";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { AccountCard } from "./account-card";
import { ActivityCard } from "./activity-card";
import { DeveloperInfoCard } from "./developer-info-card";
import { PreferencesCard } from "./preferences-card";
import { ProfileCard } from "./profile-card";

/**
 * `/account` — profile, activity, preferences and sign-out. Auth lives in the
 * browser (no server-readable cookie), so a signed-out visitor gets a sign-in
 * prompt here rather than a redirect.
 */
export function AccountView() {
  const router = useRouter();
  const { user, loading, token, apiTrusted } = useAuth();
  const { profile, error, isLoading, retry, updateDisplayName } = useProfile();

  return (
    <div className="mx-auto w-full max-w-[680px] px-4 py-4 lg:py-8">
      <h1 className="text-h1 text-fg">Account</h1>

      {loading ? (
        <div className="mt-6 flex flex-col gap-6" role="status" aria-live="polite">
          <span className="sr-only">Loading your account</span>
          <SkeletonBlock className="h-56 w-full rounded-md" />
          <SkeletonBlock className="h-40 w-full rounded-md" />
        </div>
      ) : !user ? (
        <Card className="mt-6 flex flex-col items-start gap-3 p-4 sm:p-6">
          <p className="text-body text-fg">You&rsquo;re signed out.</p>
          <p className="text-meta text-fg-muted">
            Sign in to manage your profile and see your events.
          </p>
          <Button onClick={() => router.push(signInReturnTo("/account"))}>Sign in</Button>
        </Card>
      ) : (
        <div className="mt-6 flex flex-col gap-6">
          <ProfileCard
            profile={profile}
            isLoading={isLoading}
            error={error}
            onRetry={retry}
            onSaveName={updateDisplayName}
          />
          <ActivityCard />
          <PreferencesCard />
          <AccountCard memberSince={profile?.memberSince} isLoading={isLoading} />
          <DeveloperInfoCard user={user} hasToken={Boolean(token)} apiTrusted={apiTrusted} />
        </div>
      )}
    </div>
  );
}
