import type { Metadata } from "next";
import { NewPostView } from "@/components/forum/new-post-view";

export const metadata: Metadata = { title: "New post" };

export default function NewForumPostRoute() {
  return <NewPostView />;
}
