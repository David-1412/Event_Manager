import type { Metadata } from "next";
import { DraftsPage } from "@/components/event/drafts-page";

export const metadata: Metadata = { title: "Drafts" };

export default function DraftsRoute() {
  return <DraftsPage />;
}
