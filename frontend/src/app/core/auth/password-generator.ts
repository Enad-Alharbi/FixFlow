import { isValidPassword } from './account-rules';

const UPPER = 'ABCDEFGHJKLMNPQRSTUVWXYZ'; // no I, O: easy to misread when read aloud or copied by hand
const LOWER = 'abcdefghijkmnpqrstuvwxyz'; // no l, o
const DIGITS = '23456789'; // no 0, 1
const SPECIAL = '!@#$%^&*-_=+?';
const ALL = UPPER + LOWER + DIGITS + SPECIAL;
export const GENERATED_LENGTH = 16;

/**
 * A 16-character password with at least one uppercase, lowercase, digit and special character (CLAUDE.md §7.4),
 * from the browser's cryptographically secure source (crypto.getRandomValues), never Math.random.
 */
export function generatePassword(): string {
  const chars = [pick(UPPER), pick(LOWER), pick(DIGITS), pick(SPECIAL)];
  while (chars.length < GENERATED_LENGTH) chars.push(pick(ALL));
  // Fisher–Yates shuffle, so the guaranteed characters are not always in the first four places.
  for (let i = chars.length - 1; i > 0; i--) {
    const j = randomIndex(i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  const password = chars.join('');
  if (!isValidPassword(password)) throw new Error('Generated password breaks the rules'); // cannot happen
  return password;
}

function pick(set: string): string {
  return set[randomIndex(set.length)];
}

/** Uniform 0..max-1 without modulo bias: values from the uneven tail of the 32-bit range are redrawn. */
function randomIndex(max: number): number {
  const limit = Math.floor(0x1_0000_0000 / max) * max;
  const buffer = new Uint32Array(1);
  do crypto.getRandomValues(buffer);
  while (buffer[0] >= limit);
  return buffer[0] % max;
}
