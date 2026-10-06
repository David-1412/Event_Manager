/** What the Account page shows about the signed-in person. */
export interface UserProfile {
  displayName: string;
  email: string;
  emailVerified: boolean;
  photoURL: string | null;
  /** ISO timestamp of account creation; null when the provider doesn't report it. */
  memberSince: string | null;
}

/** The three numbers on the Activity card. */
export interface UserStats {
  eventsCreated: number;
  interestedEvents: number;
  upcomingEvents: number;
}
