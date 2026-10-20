import type { Metadata } from "next";
import { ForumFeedView } from "@/components/forum/forum-feed-view";

export const metadata: Metadata = { title: "Forum" };

export default function ForumRoute() {
  return <ForumFeedView />;
}
