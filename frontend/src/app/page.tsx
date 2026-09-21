import { BrowseView } from "@/components/event/browse-view";

/**
 * `/` Browse (spec §8). `searchParams` is read server-side and passed down as a
 * string so the list has content before JS, and so the URL stays the single
 * source of filter truth. It is intentionally not `dynamic = "force-dynamic"`:
 * Next still streams the shell, and SWR owns the data.
 */
export default function HomePage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  return <BrowseViewWrapper searchParams={searchParams} />;
}

async function BrowseViewWrapper({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const params = await searchParams;
  const search = new URLSearchParams(
    Object.entries(params).flatMap(([key, value]) =>
      value === undefined ? [] : Array.isArray(value) ? value.map((v) => [key, v]) : [[key, value]],
    ),
  ).toString();
  return <BrowseView search={search} />;
}
