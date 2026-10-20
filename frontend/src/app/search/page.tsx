import type { Metadata } from "next";
import { SearchResultsView } from "@/components/layout/search-results-view";

export const metadata: Metadata = { title: "Search" };

export default function SearchRoute() {
  return <SearchResultsView />;
}
