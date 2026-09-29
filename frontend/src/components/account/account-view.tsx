"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Avatar } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Field, Input } from "@/components/ui/field";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { displayNameSchema, type DisplayNameValues } from "@/features/auth/auth-schema";

/**
 * `/account` — the signed-in identity and the two things a user can actually
 * change before the profile endpoint exists: their display name and their
 * session. It is deliberately honest about what is and isn't wired: the email
 * and avatar rows render read-only, because a control that silently no-ops is
 * worse than saying the feature hasn't landed (spec principle 5).
 *
 * The `apiTrusted` row is the same honesty applied to the backend: it reports
 * whether the API has exchanged the Firebase token for its own, which is the
 * difference between "signed in here" and "the API will accept you".
 */
export function AccountView() {
  const router = useRouter();
  const { user, loading, token, apiTrusted, logout, updateDisplayName } = useAuth();
  const [saving, setSaving] = useState(false);
  const [signingOut, setSigningOut] = useState(false);
  const [nameError, setNameError] = useState<string | null>(null);

  const form = useForm<DisplayNameValues>({
    resolver: zodResolver(displayNameSchema),
    defaultValues: { displayName: "" },
    mode: "onBlur",
  });

  // The name arrives after the session does, so the field is seeded from the
  // user rather than from a default value that would be empty on first render.
  useEffect(() => {
    if (user) form.reset({ displayName: user.displayName });
  }, [user, form]);

  if (loading) {
    return (
      <div className="mx-auto w-full max-w-[680px] px-4 py-8">
        <h1 className="text-h1 text-fg">Account</h1>
        <p className="mt-4 text-meta text-fg-muted">Checking your session.</p>
      </div>
    );
  }

  if (!user) {
    return (
      <div className="mx-auto w-full max-w-[680px] px-4 py-8">
        <h1 className="text-h1 text-fg">Account</h1>
        <Card className="mt-4 flex flex-col items-start gap-3 p-4">
          <p className="text-body text-fg">You&rsquo;re signed out.</p>
          <p className="text-meta text-fg-muted">
            Sign in to see the name players see on your events, and to change it.
          </p>
          <Button onClick={() => router.push(signInReturnTo("/account"))}>Sign in</Button>
        </Card>
      </div>
    );
  }

  async function onSaveName(values: DisplayNameValues) {
    setNameError(null);
    setSaving(true);
    const result = await updateDisplayName(values.displayName);
    setSaving(false);
    if (!result.ok) {
      setNameError(result.error ?? "Couldn't save that name.");
      return;
    }
    toast("Name updated");
  }

  async function onSignOut() {
    setSigningOut(true);
    await logout();
    // Home rather than staying on a page whose whole subject is gone.
    router.replace("/");
  }

  const firstName = user.displayName.split(" ")[0] || user.displayName;

  return (
    <div className="mx-auto w-full max-w-[680px] px-4 py-4 lg:py-8">
      <h1 className="text-h1 text-fg">Account</h1>

      <section aria-label="Who you are" className="mt-4 flex items-center gap-3">
        <Avatar name={user.displayName} src={user.photoURL} size="lg" />
        <div className="min-w-0">
          <p className="truncate text-h3 text-fg">Hey {firstName}</p>
          <p className="truncate text-meta text-fg-muted">{user.email}</p>
        </div>
      </section>

      <Card className="mt-6 p-4">
        <h2 className="text-h3 text-fg">Display name</h2>
        <p className="mt-1 text-meta text-fg-muted">
          What players see on your events and in the participant list.
        </p>
        <form
          onSubmit={(e) => void form.handleSubmit(onSaveName)(e)}
          className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-end"
        >
          <Field
            label="Name"
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
      </Card>

      <IdentityRows
        email={user.email}
        accountId={user.id ?? user.uid}
        apiIssued={Boolean(user.id)}
        apiTrusted={apiTrusted}
        hasToken={Boolean(token)}
      />

      <Card className="mt-4 flex flex-col items-start gap-3 p-4">
        <h2 className="text-h3 text-fg">Session</h2>
        <p className="text-meta text-fg-muted">
          Signing out ends the session in this browser. Your events stay.
        </p>
        <div className="flex flex-wrap gap-2">
          <Button variant="danger" loading={signingOut} onClick={() => void onSignOut()}>
            Sign out
          </Button>
          <Link
            href="/my-events"
            className="press inline-flex h-11 items-center rounded-md border border-border bg-surface px-5 text-body font-medium text-fg hover:bg-surface-2"
          >
            My events
          </Link>
        </div>
      </Card>
    </div>
  );
}

/**
 * The read-only facts. Split out so the page body stays one screen of reading;
 * `use client` at the top of the file covers it.
 */
function IdentityRows({
  email,
  accountId,
  apiIssued,
  apiTrusted,
  hasToken,
}: {
  email: string;
  accountId: string;
  apiIssued: boolean;
  apiTrusted: boolean;
  hasToken: boolean;
}) {
  return (
    <Card className="mt-4 divide-y divide-border">
      <Row label="Email" value={email} note="Confirmed by your sign-in provider." />
      <Row
        label="Account id"
        value={accountId}
        note={
          apiIssued
            ? "Issued by the API."
            : "Firebase uid — the API hasn't created a row for you yet."
        }
        mono
      />
      <Row
        label="API session"
        value={apiTrusted ? "Accepted" : hasToken ? "Not verified yet" : "None"}
        note={
          apiTrusted
            ? "The API verified your token and issued its own."
            : hasToken
              ? "This backend has no auth endpoint yet, so your token isn't verified server-side."
              : "No token — you're browsing anonymously."
        }
      />
    </Card>
  );
}

/** Read-only fact row: label, value, and the one line that explains a surprising value. */
function Row({ label, value, note, mono }: { label: string; value: string; note: string; mono?: boolean }) {
  return (
    <div className="flex flex-col gap-1 py-3 first:pt-0 last:pb-0">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <span className="text-meta font-medium text-fg">{label}</span>
        <span className={mono ? "break-all font-mono text-micro text-fg-muted" : "break-all text-meta text-fg"}>
          {value}
        </span>
      </div>
      <p className="text-meta text-fg-muted">{note}</p>
    </div>
  );
}
