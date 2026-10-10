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
import { establishSession, fetchRole, registerWithApi, toAuthUser } from "./session";
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
  /**
   * True only when the API has confirmed this caller holds the Admin role.
   *
   * False while the role is unknown, which is the correct direction: the admin
   * surface is meant to be invisible rather than merely forbidden to a Member, and a
   * caller whose role has not loaded yet is indistinguishable from a Member here.
   * Purely a rendering hint — every admin endpoint authorises on its own.
   */
  isAdmin: boolean;
  /** Moderator and Admin may publish public events without review. */
  canPublishPublicEvents: boolean;
  /** Re-read the caller's role from the API. Called after a role change so the nav
   * and the badges agree with the database without a full sign-out. */
  refreshRole: () => Promise<void>;
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

  /**
   * The current bearer token, mirrored into a ref so `refreshRole` can stay stable
   * across renders. Reading `token` from state instead would put it in the callback's
   * dependency list and hand out a new `refreshRole` on every token refresh, which
   * would re-render every consumer of the context.
   */
  const tokenRef = useRef<string | null>(null);
  useEffect(() => {
    tokenRef.current = token;
  }, [token]);

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

  /**
   * Re-read the role from the API and patch it onto the session.
   *
   * Needed because a role change made *by this very page* has no other route back
   * into React state: Firebase's `onIdTokenChanged` does not fire for a role that
   * lives on our `users` row, so without this the Admin nav item and the row badges
   * would disagree with the database until a reload. A demote of the caller's own
   * account has to be able to hide its own page.
   *
   * No-ops when signed out or when the role cannot be read — leaving the previous
   * value is better than guessing, and the server still refuses the action.
   */
  const refreshRole = useCallback(async () => {
    const current = tokenRef.current;
    if (!current) return;
    const role = await fetchRole(current);
    if (role === null) return;
    setUser((existing) => (existing ? { ...existing, role } : existing));
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      token,
      loading,
      available: isFirebaseConfigured,
      apiTrusted,
      isAdmin: user?.role === "Admin",
      canPublishPublicEvents: user?.role === "Admin" || user?.role === "Moderator",
      refreshRole,
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
      refreshRole,
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
