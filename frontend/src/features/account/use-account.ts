"use client";

import useSWR from "swr";
import { useAuth } from "@/lib/auth/auth-provider";
import { useInterests } from "@/features/events/use-interests";
import { getProfile, getUserStats } from "./account-api";

/** Shape every card needs from a fetch: skeleton while loading, retry on error. */
const options = { revalidateOnFocus: false } as const;

export function useProfile() {
  const { user, updateDisplayName } = useAuth();
  // displayName is in the key so a rename refreshes the card.
  const { data, error, isLoading, mutate } = useSWR(
    user ? ["profile", user.uid, user.displayName] : null,
    () => getProfile(user!),
    options,
  );
  return { profile: data, error, isLoading, retry: () => void mutate(), updateDisplayName };
}

export function useUserStats() {
  const { user } = useAuth();
  // Interested ids are in the key so toggling interest elsewhere refreshes the count.
  const { ids } = useInterests();
  const { data, error, isLoading, mutate } = useSWR(
    user ? ["user-stats", user.uid, ids.join(",")] : null,
    () => getUserStats(),
    options,
  );
  return { stats: data, error, isLoading, retry: () => void mutate() };
}
