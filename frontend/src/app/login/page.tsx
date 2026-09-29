import type { Metadata } from "next";
import { Suspense } from "react";
import { LoginView } from "@/components/auth/login-view";

export const metadata: Metadata = {
  title: "Sign in",
  description: "Sign in to host and join local sports games.",
};

/**
 * `LoginView` reads `?next=` through `useSearchParams`, which must sit under a
 * Suspense boundary in the App Router — without one the route can't stream its
 * shell while it waits for the client URL.
 */
export default function LoginRoute() {
  return (
    <Suspense fallback={null}>
      <LoginView />
    </Suspense>
  );
}
