import type { Metadata } from "next";
import { AdminUsersPage } from "@/components/admin/admin-users-page";

export const metadata: Metadata = { title: "Admin · Users" };

/**
 * The administrator surface's entry point. Rendered unconditionally: the guard is
 * the server's, and this page's own job is to say "you are not an Admin" honestly
 * when `GET /api/admin/users` answers 404. A client-side route guard could only
 * guess from a role that may not have loaded yet, and guessing wrong would show a
 * locked door to an administrator.
 */
export default function AdminUsersRoute() {
  return <AdminUsersPage />;
}
