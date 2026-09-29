import type { Metadata } from "next";
import { Suspense } from "react";
import { LoginView } from "@/components/auth/login-view";

export const metadata: Metadata = {
  title: "Create an account",
  description: "Create an account to host and join local sports games.",
};

/**
 * The plan lists `/login` and `/register` as two routes; they are two entries
 * into one form (`initialMode` differs) rather than two implementations, so the
 * validation, Google button and error copy can't drift apart.
 */
export default function RegisterRoute() {
  return (
    <Suspense fallback={null}>
      <LoginView initialMode="register" />
    </Suspense>
  );
}
