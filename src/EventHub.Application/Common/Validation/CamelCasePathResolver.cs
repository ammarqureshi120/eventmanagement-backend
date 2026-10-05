using System.Text;

namespace EventHub.Application.Common.Validation;

/// <summary>
/// AD-17: validation error keys are camelCase dot paths with numeric indices that match React Hook Form
/// paths (<c>attendees.3.email</c>). FluentValidation builds property paths like <c>Attendees[3].Email</c>;
/// the validation behavior rewrites every failure path through <see cref="ToPath"/>, so no validator has to
/// override property names.
/// </summary>
public static class CamelCasePathResolver
{
    /// <summary>Rewrites a FluentValidation property path into the wire key.</summary>
    public static string ToPath(string? propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return string.Empty;
        }

        var segments = new List<string>();
        var current = new StringBuilder();

        foreach (var character in propertyPath.Trim())
        {
            if (character is '.' or '[' or ']')
            {
                Flush();
            }
            else
            {
                current.Append(character);
            }
        }

        Flush();
        return string.Join('.', segments);

        void Flush()
        {
            if (current.Length > 0)
            {
                segments.Add(CamelCase(current.ToString().Trim()));
                current.Clear();
            }
        }
    }

    /// <summary><c>Email</c> → <c>email</c>, <c>EmailAddress</c> → <c>emailAddress</c>, <c>URL</c> → <c>url</c>, <c>IDNumber</c> → <c>idNumber</c>.</summary>
    public static string CamelCase(string segment)
    {
        if (segment.Length == 0 || !char.IsUpper(segment[0]))
        {
            return segment;
        }

        var chars = segment.ToCharArray();
        for (var i = 0; i < chars.Length && char.IsUpper(chars[i]); i++)
        {
            var nextIsLower = i + 1 < chars.Length && char.IsLower(chars[i + 1]);
            if (i > 0 && nextIsLower)
            {
                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }
}
