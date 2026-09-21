import type { Metadata } from "next";
import { CreateEventView } from "@/components/event/create-event-view";

export const metadata: Metadata = { title: "Create an event" };

export default function CreateRoute() {
  return <CreateEventView />;
}
