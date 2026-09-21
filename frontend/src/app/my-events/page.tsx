import type { Metadata } from "next";
import { MyEventsView } from "@/components/event/my-events-view";

export const metadata: Metadata = { title: "My events" };

export default function MyEventsRoute() {
  return <MyEventsView />;
}
