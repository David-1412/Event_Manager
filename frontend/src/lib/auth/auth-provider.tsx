"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  createUserWithEmailAndPassword,
  onIdTokenChanged,
  sendPasswordResetEmail,
  signInWithEmailAndPassword,
  signInWithPopup,
  signOut,
  updateProfile,
  type User as FirebaseUser,
} from "firebase/auth";
import { getFirebaseAuth, getGoogleProvider } from "./client";
import { describeAuthError, isAuthCancelled } from "./auth-error";
import { establishSession, registerWithApi, toAuthUser } from "./session";
import { clearAccessToken, setAccessToken } from "./token-store";
import type { AuthUser } from "./types";
import { isFirebaseConfigured } from "./config";

/**
 * `lib/auth.tsx` in the plan, split so the SDK-facing parts stay testable:
 * `client.ts` (initialisation), `session.ts` (the API exchange), this file
 * (React state). The context shape is the one the plan specifies —
 * `{user, token, loading, login, register, logout}` — plus `updateDisplayName`
 * and `resetPassword`, which the account page needs and which only make sense
 * next to the sign-in methods.
 */

export interface AuthResult {
  ok: boolean;
  /** Person-facing sentence, already mapped through describeAuthError. */
  error?: string;
}

export interface AuthContextValue {
  user: AuthUser | null;
  /** Bearer token for our API, or null when signed out. */
  token: string | null;
  /**
   * True until Firebase reports its first state. Every consumer must treat this
   * as "render nothing that depends on identity" — branching on `user === null`
   * alone shows a logged-out UI for a frame to a logged-in user.
   */
  loading: boolean;
  /** False when the Firebase config is missing: the UI says so instead of failing. */
  available: boolean;
  /** True while the API has verified the token and issued its own. */
  apiTrusted: boolean;
  login: (email: string, password: string) => Promise<AuthResult>;
  register: (displayName: string, email: string, password: string) => Promise<AuthResult>;
  loginWithGoogle: () => Promise<AuthResult>;
  logout: () => Promise<void>;
  resetPassword: (email: string) => Promise<AuthResult>;
  updateDisplayName: (displayName: string) => Promise<AuthResult>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

const NO_PROVIDER = "Sign-in isn't configured for this deployment.";

/**
 * Failsafe on `loading`. Firebase restores a session from IndexedDB and then, if
 * signed in, `establishSession` awaits `getIdToken()` — either of which can hang
 * behind a stalled network. Without a ceiling, the header, `/create`'s guard and
 * both auth pages would sit on "checking" forever. 8s is long enough that a
 * healthy restore (tens of ms) never trips it.
 */
const SESSION_TIMEOUT_MS = 8_000;

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [apiTrusted, setApiTrusted] = useState(false);
  const [loading, setLoading] = useState(true);

  /**
   * The session exchange is async, and a fast re-auth or a token refresh can
   * leave several in flight. Only the newest may write state, otherwise a stale
   * response labels the user signed-out after the fresh one signed them in.
   */
  const generation = useRef(0);

  const applySession = useCallback(async (firebaseUser: FirebaseUser | null) => {
    const ticket = ++generation.current;
    if (!firebaseUser) {
      clearAccessToken();
      setUser(null);
      setToken(null);
      setApiTrusted(false);
      return;
    }
    try {
      const session = await establishSession(firebaseUser);
      if (ticket !== generation.current) return;
      setAccessToken(session.token);
      setToken(session.token);
      setUser(session.user);
      setApiTrusted(session.apiTrusted);
    } catch {
      // The exchange failing (offline, API down) must not log anybody out:
      // Firebase still holds the session, so show the identity without the
      // API token and let the next `onIdTokenChanged` retry.
      if (ticket !== generation.current) return;
      setUser(toAuthUser(firebaseUser));
      setToken(null);
      setApiTrusted(false);
    }
  }, []);

  useEffect(() => {
    const auth = getFirebaseAuth();
    // Settles in *every* branch, including a subscription whose callback never
    // resolves (a hung token exchange): `loading` gates the header, `/create`
    // and both auth pages, so a session that cannot load must not strand them
    // on "checking". Cleared on unmount so a settled session can't be cancelled
    // by a stale timer from a previous mount.
    const settle = setTimeout(() => setLoading(false), SESSION_TIMEOUT_MS);
    if (!auth) {
      // Nothing to subscribe to, so nothing to wait for either — but not set
      // synchronously: an effect body that sets state during its own commit is
      // the cascading-render pattern React warns about, and it buys nothing here.
      clearTimeout(settle);
      queueMicrotask(() => setLoading(false));
      return;
    }
    // `onIdTokenChanged`, not `onAuthStateChanged`: it also fires when the
    // token is refreshed (~hourly), which is when a rotating API token gets
    // picked up without waiting for the next navigation.
    const unsubscribe = onIdTokenChanged(auth, (firebaseUser) => void applySession(firebaseUser));
    return () => {
      clearTimeout(settle);
      unsubscribe();
    };
  }, [applySession]);

  /** Shared tail of the email/password and Google methods. */
  const conclude = useCallback(
    async (run: () => Promise<unknown>, form: "signin" | "register"): Promise<AuthResult> => {
      if (!getFirebaseAuth()) return { ok: false, error: NO_PROVIDER };
      try {
        await run();
        return { ok: true };
      } catch (error) {
        // A dismissed popup is not a failure — the button just returns to idle.
        if (isAuthCancelled(error)) return { ok: false };
        return { ok: false, error: describeAuthError(error, form) };
      }
    },
    [],
  );

  const login = useCallback(
    (email: string, password: string) =>
      conclude(() => {
        const auth = getFirebaseAuth();
        if (!auth) throw new Error(NO_PROVIDER);
        return signInWithEmailAndPassword(auth, email.trim(), password);
      }, "signin"),
    [conclude],
  );

  const register = useCallback(
    (displayName: string, email: string, password: string) =>
      conclude(async () => {
        const auth = getFirebaseAuth();
        if (!auth) throw new Error(NO_PROVIDER);
        const credential = await createUserWithEmailAndPassword(auth, email.trim(), password);
        // The name has to live on the Firebase profile: it is what
        // `establishSession` reads before the API ever sees a register call.
        const name = displayName.trim();
        if (name) await updateProfile(credential.user, { displayName: name });
        await registerWithApi(credential.user, name);
      }, "register"),
    [conclude],
  );

  const loginWithGoogle = useCallback(
    () =>
      conclude(() => {
        const auth = getFirebaseAuth();
        if (!auth) throw new Error(NO_PROVIDER);
        return signInWithPopup(auth, getGoogleProvider());
      }, "signin"),
    [conclude],
  );

  const logout = useCallback(async () => {
    const auth = getFirebaseAuth();
    // Clear first: the UI must leave the authenticated state even if the
    // network call behind `signOut` fails, or the header would keep offering
    // "Sign out" on a session the user believes is gone.
    ++generation.current;
    clearAccessToken();
    setUser(null);
    setToken(null);
    setApiTrusted(false);
    if (auth) await signOut(auth).catch(() => undefined);
    setLoading(false);
  }, []);

  const resetPassword = useCallback(async (email: string): Promise<AuthResult> => {
    const auth = getFirebaseAuth();
    if (!auth) return { ok: false, error: NO_PROVIDER };
    try {
      await sendPasswordResetEmail(auth, email.trim());
      return { ok: true };
    } catch (error) {
      return { ok: false, error: describeAuthError(error) };
    }
  }, []);

  /**
   * Local-only rename. There is no `PUT /api/profile` yet, so this writes the
   * Firebase profile and our own state; the API row catches up when the
   * endpoint exists and `establishSession` runs again on the next token change.
   */
  const updateDisplayName = useCallback(async (displayName: string): Promise<AuthResult> => {
    const auth = getFirebaseAuth();
    const name = displayName.trim();
    if (!auth?.currentUser) return { ok: false, error: "You're signed out. Sign in again." };
    if (!name) return { ok: false, error: "Enter a name." };
    try {
      await updateProfile(auth.currentUser, { displayName: name });
    } catch (error) {
      return { ok: false, error: describeAuthError(error) };
    }
    // `onIdTokenChanged` does not fire for a profile edit, so mirror it here.
    setUser((current) => (current ? { ...current, displayName: name } : current));
    return { ok: true };
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      token,
      loading,
      available: isFirebaseConfigured,
      apiTrusted,
      login,
      register,
      loginWithGoogle,
      logout,
      resetPassword,
      updateDisplayName,
    }),
    [
      user,
      token,
      loading,
      apiTrusted,
      login,
      register,
      loginWithGoogle,
      logout,
      resetPassword,
      updateDisplayName,
    ],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

/** Throws when used outside the provider — a wiring bug, not a runtime state. */
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth must be used inside <AuthProvider>");
  return context;
}
