/**
 * The Firebase web config for the `evnt` project, filled from your snippet with
 * one deliberate change: every value is read from `NEXT_PUBLIC_FIREBASE_*`
 * first and falls back to the literal. These identifiers are not secrets (the
 * API key ships to the browser either way — it is a project *identifier*), but
 * hard-coding them means the preview/deploy environments cannot point at a
 * second Firebase project without a rebuild, and Firebase keys get rotated.
 * Fill `.env.local` in those environments and the literals stop being used.
 *
 * Analytics is deliberately not initialised here: it needs the `measurementId`
 * and it starts firing pageviews the moment the module is imported, which is
 * behaviour nobody asked for and an audit finding waiting to happen. Wire it
 * with `getAnalytics(app)` behind a consent/feature flag when it is wanted.
 */
import type { FirebaseOptions } from "firebase/app";

export const firebaseConfig: FirebaseOptions = {
  apiKey: process.env.NEXT_PUBLIC_FIREBASE_API_KEY ?? "AIzaSyBVPxWdPyMJnBTpETekn0_VVZCGOop3O84",
  authDomain: process.env.NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN ?? "evnt-bb3b5.firebaseapp.com",
  projectId: process.env.NEXT_PUBLIC_FIREBASE_PROJECT_ID ?? "evnt-bb3b5",
  storageBucket: process.env.NEXT_PUBLIC_FIREBASE_STORAGE_BUCKET ?? "evnt-bb3b5.firebasestorage.app",
  messagingSenderId: process.env.NEXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID ?? "189692765154",
  appId: process.env.NEXT_PUBLIC_FIREBASE_APP_ID ?? "1:189692765154:web:c41645cfe5a423af66cfbb",
  measurementId: process.env.NEXT_PUBLIC_FIREBASE_MEASUREMENT_ID ?? "G-2E0GR14BZ7",
};

/**
 * True when enough of the config exists to talk to Firebase Auth. The client
 * refuses to initialise without it (see `client.ts`) rather than throwing from
 * module scope and taking the whole route down.
 */
export const isFirebaseConfigured = Boolean(firebaseConfig.apiKey && firebaseConfig.projectId);
