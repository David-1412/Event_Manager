import { BrowseView } from "@/components/event/browse-view";

/**
 * `/` Browse (spec §8). `searchParams` is read server-side and passed down as a
 * string so the list has content before JS, and so the URL stays the single
 * source of filter truth. It is intentionally not `dynamic = "force-dynamic"`:
 * Next still streams the shell, and SWR owns the data.
 */
export default function HomePage() {
  return <BrowseView />;
}
