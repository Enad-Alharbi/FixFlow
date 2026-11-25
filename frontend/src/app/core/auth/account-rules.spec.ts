import { checkPassword, isValidEmail, isValidPassword, isValidUsername } from './account-rules';

// The same cases as the backend's UserRulesTests, so both sides provably agree.
describe('account rules (mirror of the backend)', () => {
  it.each([
    ['Sh0rt!x', 'length'],
    ['nouppercase1!', 'upper'],
    ['NOLOWERCASE1!', 'lower'],
    ['NoDigitsHere!', 'digit'],
    ['NoSpecial123', 'special'],
    [' Leading1!', 'noOuterSpaces'],
    ['Trailing1! ', 'noOuterSpaces'],
    ['Aa1!' + 'x'.repeat(125), 'length'], // 129 characters
  ] as const)('rejects %j (fails %s)', (password, rule) => {
    expect(checkPassword(password)[rule]).toBe(false);
    expect(isValidPassword(password)).toBe(false);
  });

  it('accepts a password with a space in the middle', () => {
    expect(isValidPassword('A middle space is fine1!')).toBe(true);
  });

  it('counts an Arabic letter as a special character, like ASP.NET Identity', () => {
    expect(checkPassword('Abcdefg1ب').special).toBe(true);
  });

  it.each(['ab', '1abc', '_abc', 'has space', 'ahmed@site', 'أحمد', 'a' + '1'.repeat(30)])(
    'rejects username %j',
    (username) => expect(isValidUsername(username)).toBe(false),
  );

  it.each(['abc', 'acme.supervisor', 'nahda_tech-2'])('accepts username %j', (username) =>
    expect(isValidUsername(username)).toBe(true),
  );

  it.each(['not-an-email', 'a b@example.com', 'a@b', 'x'.repeat(250) + '@b.co'])(
    'rejects email %j',
    (email) => expect(isValidEmail(email)).toBe(false),
  );
});
