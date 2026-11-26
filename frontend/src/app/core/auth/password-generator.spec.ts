import { describe, expect, it, vi } from 'vitest';
import { checkPassword } from './account-rules';
import { GENERATED_LENGTH, generatePassword } from './password-generator';

describe('generatePassword', () => {
  it('always meets every password rule and is 16 characters long', () => {
    for (let i = 0; i < 500; i++) {
      const password = generatePassword();
      expect(password).toHaveLength(GENERATED_LENGTH);
      expect(Object.values(checkPassword(password)).every(Boolean)).toBe(true);
    }
  });

  it('uses the cryptographic random source, never Math.random', () => {
    const crypto = vi.spyOn(globalThis.crypto, 'getRandomValues');
    const math = vi.spyOn(Math, 'random');
    generatePassword();
    expect(crypto).toHaveBeenCalled();
    expect(math).not.toHaveBeenCalled();
    vi.restoreAllMocks();
  });

  it('gives a different password each time', () => {
    expect(new Set(Array.from({ length: 50 }, generatePassword)).size).toBe(50);
  });
});
