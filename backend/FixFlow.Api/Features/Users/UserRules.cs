using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace FixFlow.Api.Features.Users;

/// <summary>
/// Account rules from CLAUDE.md §7.4. The backend is the authority; the frontend mirrors these exact rules.
/// Identity's built-in options cover what they can (see Program.cs); the validators below add the rest.
/// Every failure carries a stable error code that the frontend localizes.
/// </summary>
public static partial class UserRules
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 30;
    public const string UsernameAllowedCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";
    public const int EmailMaxLength = 254;
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 100;

    // Optional "+", then digits with single spaces or dashes between them; 7–15 digits in total (E.164 max).
    [GeneratedRegex(@"^\+?[0-9]+([ -]?[0-9]+)*$")]
    public static partial Regex PhoneFormat();

    public static bool IsValidPhone(string phone)
    {
        var digits = phone.Count(char.IsAsciiDigit);
        return PhoneFormat().IsMatch(phone) && digits is >= 7 and <= 15;
    }

    public static bool IsValidEmail(string email) =>
        email.Length <= EmailMaxLength
        && !email.Any(char.IsWhiteSpace)
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email; // rejects display-name forms like "Name <a@b.c>"
}

/// <summary>Username, email, display name, and phone rules that Identity's own UserValidator doesn't cover.</summary>
public class UserRulesValidator : IUserValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var errors = new List<IdentityError>();

        // Allowed characters are checked by Identity (UserOptions.AllowedUserNameCharacters).
        var username = user.UserName ?? "";
        if (username.Length is < UserRules.UsernameMinLength or > UserRules.UsernameMaxLength
            || !char.IsAsciiLetter(username.FirstOrDefault()))
            errors.Add(UserErrors.UsernameInvalid);

        if (!UserRules.IsValidEmail(user.Email ?? ""))
            errors.Add(UserErrors.EmailInvalid);

        var displayName = user.DisplayName ?? "";
        if (displayName != displayName.Trim()
            || displayName.Length is < UserRules.DisplayNameMinLength or > UserRules.DisplayNameMaxLength)
            errors.Add(UserErrors.DisplayNameInvalid);

        if (user.PhoneNumber is { } phone && !UserRules.IsValidPhone(phone))
            errors.Add(UserErrors.PhoneInvalid);

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }
}

/// <summary>Password rules that Identity's PasswordOptions can't express: a maximum length and no outer spaces.</summary>
public class PasswordRulesValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var ok = password is { Length: > 0 and <= UserRules.PasswordMaxLength }
            && !char.IsWhiteSpace(password[0])
            && !char.IsWhiteSpace(password[^1]);
        return Task.FromResult(ok ? IdentityResult.Success : IdentityResult.Failed(UserErrors.PasswordTooWeak));
    }
}

public static class UserErrors
{
    public static readonly IdentityError PasswordTooWeak = new() { Code = "user.password_too_weak", Description = "The password does not meet the password rules." };
    public static readonly IdentityError CurrentPasswordIncorrect = new() { Code = "user.current_password_incorrect", Description = "The current password is incorrect." };
    public static readonly IdentityError UsernameInvalid = new() { Code = "user.username_invalid", Description = "The username does not meet the username rules." };
    public static readonly IdentityError UsernameTaken = new() { Code = "user.username_taken", Description = "The username is already taken." };
    public static readonly IdentityError EmailInvalid = new() { Code = "user.email_invalid", Description = "The email address is not valid." };
    public static readonly IdentityError EmailTaken = new() { Code = "user.email_taken", Description = "The email address is already taken." };
    public static readonly IdentityError DisplayNameInvalid = new() { Code = "user.display_name_invalid", Description = "The display name must be 2–100 characters." };
    public static readonly IdentityError PhoneInvalid = new() { Code = "user.phone_invalid", Description = "The phone number is not valid." };
}

/// <summary>Replaces Identity's built-in error codes (e.g. "PasswordRequiresDigit") with FixFlow's stable codes.</summary>
public class FixFlowIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => UserErrors.PasswordTooWeak;
    public override IdentityError PasswordRequiresDigit() => UserErrors.PasswordTooWeak;
    public override IdentityError PasswordRequiresLower() => UserErrors.PasswordTooWeak;
    public override IdentityError PasswordRequiresUpper() => UserErrors.PasswordTooWeak;
    public override IdentityError PasswordRequiresNonAlphanumeric() => UserErrors.PasswordTooWeak;
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => UserErrors.PasswordTooWeak;
    public override IdentityError PasswordMismatch() => UserErrors.CurrentPasswordIncorrect;
    public override IdentityError InvalidUserName(string? userName) => UserErrors.UsernameInvalid;
    public override IdentityError DuplicateUserName(string userName) => UserErrors.UsernameTaken;
    public override IdentityError InvalidEmail(string? email) => UserErrors.EmailInvalid;
    public override IdentityError DuplicateEmail(string email) => UserErrors.EmailTaken;
}
