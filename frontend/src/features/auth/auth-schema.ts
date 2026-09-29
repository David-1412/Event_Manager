import { z } from "zod";

/**
 * Credentials validation, kept in `features/` next to the other data-layer
 * schemas so it is testable without rendering (spec §8's "validation mirrors the
 * API's" rule applies to auth too).
 *
 * Deliberately laxer than it looks. The form checks presence and shape; it does
 * **not** claim to know whether a password is strong enough or an email exists —
 * only Firebase does, and pre-empting it with a rule the server doesn't share
 * just rejects valid accounts. Firebase's own minimum is 6 characters, so that
 * is the length enforced here and nothing above it.
 *
 * `z.string().email()` is deprecated in zod v4 in favour of the top-level
 * `z.email()`, which is what this uses. Every schema here is an object at the
 * top level because react-hook-form's `zodResolver` is typed for object schemas
 * (`FieldValues`), which is why the account page's name check is an object
 * wrapper rather than a bare `z.string()`.
 */

/** Mirrors `users.name`'s 120-char column so a long name can't 500 later. */
export const MIN_NAME_LENGTH = 2;
export const MAX_NAME_LENGTH = 120;
export const MIN_PASSWORD_LENGTH = 6;

const emailField = z.email("Enter an email address").max(320, "That email is too long");

const passwordField = z
  .string()
  .min(MIN_PASSWORD_LENGTH, `Use at least ${MIN_PASSWORD_LENGTH} characters`)
  .max(200, "That password is too long");

const nameField = z
  .string()
  .trim()
  .min(MIN_NAME_LENGTH, `Use at least ${MIN_NAME_LENGTH} characters`)
  .max(MAX_NAME_LENGTH, `Keep it under ${MAX_NAME_LENGTH} characters`);

export const signInSchema = z.object({
  email: emailField,
  password: z.string().min(1, "Enter your password"),
});

export type SignInValues = z.output<typeof signInSchema>;

export const registerSchema = z
  .object({
    displayName: nameField,
    email: emailField,
    password: passwordField,
    confirm: z.string().min(1, "Re-enter your password"),
  })
  .refine((values) => values.password === values.confirm, {
    path: ["confirm"],
    message: "Both passwords must match",
  });

export type RegisterValues = z.output<typeof registerSchema>;

export const resetSchema = z.object({ email: emailField });
export type ResetValues = z.output<typeof resetSchema>;

/**
 * The account page's editable name, wrapped in an object so it is a
 * `zodResolver`-compatible form schema. Same bounds as registration so the two
 * never disagree about what a valid name is.
 */
export const displayNameSchema = z.object({ displayName: nameField });
export type DisplayNameValues = z.output<typeof displayNameSchema>;
