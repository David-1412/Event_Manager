import type { Metadata } from "next";
import { AccountView } from "@/components/account/account-view";

export const metadata: Metadata = {
  title: "Account",
  description: "Your name, your sign-in, and your session.",
};

/**
 * Not a server-side redirect when signed out: auth lives in the browser (Firebase
 * SDK, no cookie the server can read), so a middleware check would mean
 * duplicating the session into a cookie purely to satisfy a redirect. The view
 * renders a sign-in prompt instead, which is also what a user who deliberately
 * signed out wants to see.
 */
export default function AccountRoute() {
  return <AccountView />;
}
