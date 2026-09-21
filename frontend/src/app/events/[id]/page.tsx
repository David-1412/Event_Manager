import type { Metadata } from "next";
import { EventDetailPage } from "@/components/event/event-detail-view";

export const metadata: Metadata = { title: "Event" };

export default function EventRoute() {
  return <EventDetailPage />;
}
