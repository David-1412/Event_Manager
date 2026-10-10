import type { Metadata } from "next";
import { AdminEventsPage } from "@/components/admin/admin-events-page";

export const metadata: Metadata = { title: "Admin · Events" };

export default function AdminEventsRoute() {
  return <AdminEventsPage />;
}
