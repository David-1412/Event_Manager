"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Field, Input } from "@/components/ui/field";
import { GoogleButton } from "@/components/auth/google-button";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import {
  registerSchema,
  signInSchema,
  type RegisterValues,
  type SignInValues,
} from "@/features/auth/auth-schema";

/**
 * `/login` (spec §8, plan §13): email/password and Google, with register and
 * password reset as modes of the same screen rather than separate routes —
 * people hop between the three constantly, and a page load per hop loses the
 * email they just typed. `/register` renders this too, starting in the other
 * mode, so there is one form to keep correct.
 *
 * The form never throws on a failed Firebase call: `useAuth` resolves with a
 * mapped sentence, and the form shows it. `?next=` is honoured after sign-in so
 * a guarded page can send the user straight back to what they were doing.
 */

type Mode = "signin" | "register";

/** Only same-origin paths, so `?next=https://evil.test` cannot become a redirect. */
function safeNext(value: string | null): string {
  if (!value || !value.startsWith("/") || value.startsWith("//")) return "/";
  return value;
}

export function LoginView({ initialMode = "signin" }: { initialMode?: Mode }) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const next = safeNext(searchParams.get("next"));
  const { user, loading, available, login, register, loginWithGoogle, resetPassword } = useAuth();

  const [mode, setMode] = useState<Mode>(initialMode);
  const [googleBusy, setGoogleBusy] = useState(false);
  const [formBusy, setFormBusy] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [resetNotice, setResetNotice] = useState<string | null>(null);

  /**
   * Both forms exist at once rather than one form whose schema swaps: switching
   * `resolver` mid-life keeps stale errors and touched flags from the other
   * mode, and it leaves `register`/`setValue` as a union of two field shapes
   * that TypeScript can't call. Each field binds to the form that owns it.
   */
  const signInForm = useForm<SignInValues>({
    resolver: zodResolver(signInSchema),
    defaultValues: { email: "", password: "" },
    mode: "onBlur",
  });
  const registerForm = useForm<RegisterValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { displayName: "", email: "", password: "", confirm: "" },
    mode: "onBlur",
  });

  // Already signed in (or the session restored while you sat here) — no reason
  // to show a form. Waits for `loading` so a restoring session isn't bounced.
  const redirected = useRef(false);
  useEffect(() => {
    if (loading || redirected.current) return;
    if (user) {
      redirected.current = true;
      router.replace(next);
    }
  }, [loading, user, next, router]);

  function switchMode(nextMode: Mode) {
    setMode(nextMode);
    setAuthError(null);
    setResetNotice(null);
    // Keep whatever email is already typed — the whole point of one screen.
    // Written as two branches rather than a `from`/`to` pair: react-hook-form's
    // `getValues`/`setValue` are generic over the field name, so a union of two
    // form instances only accepts the field names in *both* shapes, and `email`
    // resolves to `never` there.
    if (nextMode === "register") {
      const email = signInForm.getValues("email");
      if (email && !registerForm.getValues("email")) registerForm.setValue("email", email);
    } else {
      const email = registerForm.getValues("email");
      if (email && !signInForm.getValues("email")) signInForm.setValue("email", email);
    }
  }

  async function onSubmit() {
    setAuthError(null);
    setResetNotice(null);
    const valid = mode === "signin" ? await signInForm.trigger() : await registerForm.trigger();
    if (!valid) return;
    setFormBusy(true);
    const result =
      mode === "signin"
        ? await login(signInForm.getValues("email"), signInForm.getValues("password"))
        : await register(
            registerForm.getValues("displayName"),
            registerForm.getValues("email"),
            registerForm.getValues("password"),
          );
    setFormBusy(false);
    if (!result.ok) {
      setAuthError(result.error ?? "Something went wrong. Please try again.");
      return;
    }
    toast(mode === "signin" ? "Signed in" : "Account created");
    router.replace(next);
  }

  async function onGoogle() {
    setAuthError(null);
    setGoogleBusy(true);
    const result = await loginWithGoogle();
    setGoogleBusy(false);
    if (!result.ok) {
      // No message means the popup was dismissed — nothing worth reporting.
      if (result.error) setAuthError(result.error);
      return;
    }
    toast("Signed in with Google");
    router.replace(next);
  }

  async function onForgotPassword() {
    if (!(await signInForm.trigger("email"))) return;
    setAuthError(null);
    const result = await resetPassword(signInForm.getValues("email"));
    // Deliberately doesn't confirm the address exists — an auth form that says
    // "no account for that email" is an account-enumeration oracle.
    setResetNotice(
      result.ok
        ? "If that email has an account, a reset link is on its way."
        : (result.error ?? "Couldn't send the reset email."),
    );
  }

  /**
   * Rendered after `loading` clears rather than flashing the form to a user
   * whose session is restoring: swapping the whole card one frame after
   * hydration is a visible content jump on the page whose entire job is a
   * redirect (spec §11 bans exactly this).
   */
  if (loading) {
    return (
      <AuthShell title="Checking your session">
        <p className="text-meta text-fg-muted">One moment — if you&rsquo;re already signed in we&rsquo;ll take you back.</p>
      </AuthShell>
    );
  }

  if (user) return null;

  const isSignIn = mode === "signin";

  /**
   * `register("email")` on a *union* of the two form instances is not callable —
   * TypeScript only keeps the overloads valid for both field shapes. Resolving
   * the instance inside a branch per call keeps each call monomorphic, which is
   * the same reason `switchMode` is two branches above.
   */
  const emailRegistration = isSignIn ? signInForm.register("email") : registerForm.register("email");
  const passwordRegistration = isSignIn
    ? signInForm.register("password")
    : registerForm.register("password");

  return (
    <AuthShell title={isSignIn ? "Sign in" : "Create your account"}>
      {!available && (
        <div role="alert" className="rounded-md border border-warn bg-surface-2 p-3 text-meta text-fg">
          Firebase isn&rsquo;t configured for this deployment, so sign-in is unavailable. Set the{" "}
          <code className="rounded-sm bg-surface px-1 text-micro">NEXT_PUBLIC_FIREBASE_*</code>{" "}
          values in <code className="rounded-sm bg-surface px-1 text-micro">.env.local</code>.
        </div>
      )}

      <GoogleButton onClick={() => void onGoogle()} loading={googleBusy} disabled={!available || formBusy} />

      <OrLabel />

      <form
        onSubmit={(e) => {
          e.preventDefault();
          void onSubmit();
        }}
        className="flex flex-col gap-4"
        noValidate
      >
        {!isSignIn && (
          <Field label="Name" error={registerForm.formState.errors.displayName?.message} required>
            {({ id, describedBy, invalid }) => (
              <Input
                id={id}
                aria-describedby={describedBy}
                aria-invalid={invalid}
                autoComplete="name"
                placeholder="Sam Nguyen"
                {...registerForm.register("displayName")}
              />
            )}
          </Field>
        )}

        <Field
          label="Email"
          error={
            isSignIn
              ? signInForm.formState.errors.email?.message
              : registerForm.formState.errors.email?.message
          }
          required
        >
          {({ id, describedBy, invalid }) => (
            <Input
              id={id}
              type="email"
              inputMode="email"
              autoComplete="email"
              aria-describedby={describedBy}
              aria-invalid={invalid}
              placeholder="you@example.com"
              {...emailRegistration}
            />
          )}
        </Field>

        <Field
          label="Password"
          error={
            isSignIn
              ? signInForm.formState.errors.password?.message
              : registerForm.formState.errors.password?.message
          }
          hint={isSignIn ? undefined : "At least 6 characters."}
          required
        >
          {({ id, describedBy, invalid }) => (
            <Input
              id={id}
              type="password"
              autoComplete={isSignIn ? "current-password" : "new-password"}
              aria-describedby={describedBy}
              aria-invalid={invalid}
              {...passwordRegistration}
            />
          )}
        </Field>

        {!isSignIn && (
          <Field label="Confirm password" error={registerForm.formState.errors.confirm?.message} required>
            {({ id, describedBy, invalid }) => (
              <Input
                id={id}
                type="password"
                autoComplete="new-password"
                aria-describedby={describedBy}
                aria-invalid={invalid}
                {...registerForm.register("confirm")}
              />
            )}
          </Field>
        )}

        {authError && (
          <p role="alert" className="rounded-md border border-danger bg-surface-2 p-3 text-meta text-danger">
            {authError}
          </p>
        )}
        {resetNotice && (
          <p role="status" className="rounded-md border border-border bg-surface-2 p-3 text-meta text-fg-muted">
            {resetNotice}
          </p>
        )}

        <Button type="submit" fullWidth loading={formBusy} disabled={!available}>
          {isSignIn ? "Sign in" : "Create account"}
        </Button>
      </form>

      <div className="mt-4 flex flex-col gap-2 text-meta text-fg-muted">
        {isSignIn && (
          <button
            type="button"
            onClick={() => void onForgotPassword()}
            className="press self-start text-brand-600 hover:underline"
          >
            Forgot password?
          </button>
        )}
        <button
          type="button"
          onClick={() => switchMode(isSignIn ? "register" : "signin")}
          className="press self-start text-brand-600 hover:underline"
        >
          {isSignIn ? "No account yet? Create one" : "Already have an account? Sign in"}
        </button>
        <Link href="/" className="press self-start hover:underline">
          Back to browse
        </Link>
      </div>
    </AuthShell>
  );
}

/** Hairline divider with the word in it — the conventional "or" between providers. */
function OrLabel() {
  return (
    <div className="flex items-center gap-3" aria-hidden>
      <span className="h-px flex-1 bg-border" />
      <span className="text-micro uppercase tracking-wide text-fg-muted">or</span>
      <span className="h-px flex-1 bg-border" />
    </div>
  );
}

/** One centred column, the same measure the other single-form pages use (spec §5). */
function AuthShell({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="mx-auto w-full max-w-[420px] px-4 py-8">
      <h1 className="text-h1 text-fg">{title}</h1>
      <Card className="mt-4 flex flex-col gap-4 p-4">{children}</Card>
    </div>
  );
}

