"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Field, Input } from "@/components/ui/field";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { displayNameSchema, type DisplayNameValues } from "@/features/auth/auth-schema";
import { RoleBadge } from "@/components/admin/role-badge";
import { useAuth, type AuthResult } from "@/lib/auth/auth-provider";
import type { UserProfile } from "@/types/account";
import { CardError } from "./card-error";

interface ProfileCardProps {
  profile: UserProfile | undefined;
  isLoading: boolean;
  error: unknown;
  onRetry: () => void;
  onSaveName: (displayName: string) => Promise<AuthResult>;
}

export function ProfileCard({ profile, isLoading, error, onRetry, onSaveName }: ProfileCardProps) {
  return (
    <Card className="p-4 sm:p-6">
      <h2 className="text-h3 text-fg">Profile</h2>
      <div className="mt-4">
        {error ? (
          <CardError message="We couldn't load your profile." onRetry={onRetry} />
        ) : isLoading || !profile ? (
          <ProfileSkeleton />
        ) : (
          <ProfileBody profile={profile} onSaveName={onSaveName} />
        )}
      </div>
    </Card>
  );
}

function ProfileSkeleton() {
  return (
    <div role="status" aria-live="polite" className="flex flex-col gap-4">
      <span className="sr-only">Loading your profile</span>
      <div className="flex items-center gap-4">
        <SkeletonBlock className="h-14 w-14 rounded-full" />
        <div className="flex flex-col gap-2">
          <SkeletonBlock className="h-5 w-40" />
          <SkeletonBlock className="h-3.5 w-52" />
        </div>
      </div>
      <SkeletonBlock className="h-11 w-full" />
    </div>
  );
}

function ProfileBody({
  profile,
  onSaveName,
}: {
  profile: UserProfile;
  onSaveName: ProfileCardProps["onSaveName"];
}) {
  // From the API's own record (GET /api/auth/me), not the Firebase profile, so it
  // shows the role the next request will actually be authorised as.
  const role = useAuth().user?.role;
  const [saving, setSaving] = useState(false);
  const [nameError, setNameError] = useState<string | null>(null);

  const form = useForm<DisplayNameValues>({
    resolver: zodResolver(displayNameSchema),
    defaultValues: { displayName: profile.displayName },
    mode: "onBlur",
  });

  // Re-seed after a save (or a refetch) so the form is clean against the new name.
  useEffect(() => {
    form.reset({ displayName: profile.displayName });
  }, [profile.displayName, form]);

  async function onSubmit(values: DisplayNameValues) {
    setNameError(null);
    setSaving(true);
    const result = await onSaveName(values.displayName);
    setSaving(false);
    if (!result.ok) {
      setNameError(result.error ?? "Couldn't save that name.");
      return;
    }
    toast("Name updated");
  }

  return (
    <div className="flex flex-col gap-5">
      <div className="flex items-center gap-4">
        <Avatar name={profile.displayName} src={profile.photoURL} size="lg" />
        <div className="min-w-0">
          <p className="truncate text-h3 text-fg">{profile.displayName}</p>
          <div className="mt-1 flex flex-wrap items-center gap-2">
            <p className="truncate text-meta text-fg-muted">{profile.email}</p>
            {profile.emailVerified && <Badge tone="brand">Verified</Badge>}
            <RoleBadge role={role} />
          </div>
        </div>
      </div>

      <form
        onSubmit={(e) => void form.handleSubmit(onSubmit)(e)}
        className="flex flex-col gap-3 sm:flex-row sm:items-end"
      >
        <Field
          label="User Name"
          error={nameError ?? form.formState.errors.displayName?.message}
          className="flex-1"
          required
        >
          {({ id, describedBy, invalid }) => (
            <Input
              id={id}
              aria-describedby={describedBy}
              aria-invalid={invalid}
              autoComplete="name"
              {...form.register("displayName")}
            />
          )}
        </Field>
        <Button type="submit" loading={saving} disabled={!form.formState.isDirty}>
          Save
        </Button>
      </form>

      <Field label="Email" className="max-w-full">
        {({ id }) => <Input id={id} value={profile.email} readOnly disabled autoComplete="email" />}
      </Field>
    </div>
  );
}
