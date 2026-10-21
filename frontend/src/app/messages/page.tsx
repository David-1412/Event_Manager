import type { Metadata } from "next";
import { MessagesView } from "@/components/messages/messages-view";

export const metadata: Metadata = { title: "Messages" };

export default function MessagesRoute() {
  return <MessagesView />;
}
