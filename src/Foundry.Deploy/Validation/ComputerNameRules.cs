using System.Text;

namespace Foundry.Deploy.Validation;

public static class ComputerNameRules
{
    public const int MaxLength = 15;
    public const string FallbackName = "PC";
    public const string ValidationMessage = "Computer name must contain 1 to 15 characters using A-Z, a-z, 0-9, or -.";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(MaxLength);
        foreach (char character in value)
        {
            if (!IsAllowedCharacter(character))
            {
                continue;
            }

            builder.Append(character);
            if (builder.Length >= MaxLength)
            {
                break;
            }
        }

        return builder.ToString();
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsAllowedCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsAllowedText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsAllowedCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    public static string GetValidationMessage(string? value)
    {
        return IsValid(value)
            ? string.Empty
            : ValidationMessage;
    }

    public static bool IsAllowedCharacter(char character)
    {
        return (character >= 'A' && character <= 'Z') ||
               (character >= 'a' && character <= 'z') ||
               (character >= '0' && character <= '9') ||
               character == '-';
    }
}
