/**
 * Account rules from CLAUDE.md §7.4, mirrored from the backend (Features/Users/UserRules.cs) for instant feedback.
 * The backend is the authority: it re-checks everything and returns the same error codes.
 */

export type PasswordRule = 'length' | 'upper' | 'lower' | 'digit' | 'special' | 'noOuterSpaces';

export const PASSWORD_MIN = 8;
export const PASSWORD_MAX = 128;

const PASSWORD_CHECKS: Record<PasswordRule, (password: string) => boolean> = {
  length: (p) => p.length >= PASSWORD_MIN && p.length <= PASSWORD_MAX,
  upper: (p) => /[A-Z]/.test(p),
  lower: (p) => /[a-z]/.test(p),
  digit: (p) => /[0-9]/.test(p),
  // Same definition as ASP.NET Identity: anything that isn't A–Z, a–z or 0–9 (so Arabic letters count too).
  special: (p) => /[^A-Za-z0-9]/.test(p),
  noOuterSpaces: (p) => p.length > 0 && p.trim() === p,
};

export const PASSWORD_RULES = Object.keys(PASSWORD_CHECKS) as PasswordRule[];

/** Each rule and whether the password meets it — drives the live checklist. */
export function checkPassword(password: string): Record<PasswordRule, boolean> {
  return Object.fromEntries(
    PASSWORD_RULES.map((rule) => [rule, PASSWORD_CHECKS[rule](password)]),
  ) as Record<PasswordRule, boolean>;
}

export const isValidPassword = (password: string) =>
  PASSWORD_RULES.every((rule) => PASSWORD_CHECKS[rule](password));

/** 3–30 characters; ASCII letters, digits, "." "_" "-"; starts with a letter. Compared case-insensitively. */
export const isValidUsername = (username: string) =>
  /^[A-Za-z][A-Za-z0-9._-]{2,29}$/.test(username);

/**
 * At most 254 characters, no spaces, one "@" with text on both sides and a dot in the domain.
 * shortcut: looser than the server's MailAddress check; the server's `user.email_invalid` covers the rest.
 */
export const isValidEmail = (email: string) =>
  email.length <= 254 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
