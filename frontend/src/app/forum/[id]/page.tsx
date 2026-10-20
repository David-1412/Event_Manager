import type { Metadata } from "next";
import { ForumPostView } from "@/components/forum/forum-post-view";

export const metadata: Metadata = { title: "Forum post" };

export default function ForumPostRoute() {
  return <ForumPostView />;
}
