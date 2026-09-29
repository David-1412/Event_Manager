import { getApps, initializeApp, type FirebaseApp } from "firebase/app";
import { getAuth, GoogleAuthProvider, type Auth } from "firebase/auth";
import { firebaseConfig, isFirebaseConfigured } from "./config";

/**
 * Firebase Auth is client-only in this milestone (spec §13): the browser owns
 * the session, the .NET API verifies the ID token server-side. Nothing here
 * imports `firebase/auth` at module scope in a server component, which is why
 * it lives in `lib` rather than in a component file.
 */

let app: FirebaseApp | null = null;
let auth: Auth | null = null;

/**
 * Lazily initialised and cached. `initializeApp` at module scope would run
 * during the server render of every page that transitively imports auth, and
 * throws outright on a missing config — a configuration problem would take the
 * route down instead of degrading to "sign-in unavailable".
 */
export function getFirebaseApp(): FirebaseApp | null {
  if (!isFirebaseConfigured) return null;
  if (app) return app;
  const existing = getApps();
  app = existing[0] ?? initializeApp(firebaseConfig);
  return app;
}

export function getFirebaseAuth(): Auth | null {
  const instance = getFirebaseApp();
  if (!instance) return null;
  if (!auth) auth = getAuth(instance);
  return auth;
}

/**
 * Single provider instance. Scopes are left at the Auth default (email, and
 * `profile`/`openid`) — the display name and photo come from the Google
 * account, which is all the profile needs at this stage.
 */
let provider: GoogleAuthProvider | null = null;

export function getGoogleProvider(): GoogleAuthProvider {
  if (!provider) {
    provider = new GoogleAuthProvider();
    provider.setCustomParameters({ prompt: "select_account" });
  }
  return provider;
}
