using System.Text.RegularExpressions;

namespace Autohand.CodeAgentSdk;

public static partial class JsonOutput
{
    public static string WithJsonInstruction(string prompt, JsonRunOptions? options = null)
    {
        var parts = new List<string>
        {
            prompt,
            string.Empty,
            "Return only valid JSON.",
            "Do not wrap the response in Markdown.",
            "Do not include commentary outside the JSON value.",
        };

        if (!string.IsNullOrWhiteSpace(options?.SchemaName))
        {
            parts.Add($"The JSON value should satisfy: {options.SchemaName}.");
        }

        if (options?.Schema is not null)
        {
            parts.Add("Use this JSON schema or example shape:");
            parts.Add(JsonSerializer.Serialize(options.Schema, options.SerializerOptions));
        }

        if (!string.IsNullOrWhiteSpace(options?.OutputInstructions))
        {
            parts.Add(options.OutputInstructions);
        }

        return string.Join('\n', parts);
    }

    public static T Parse<T>(string text, JsonRunOptions? options = null)
    {
        var json = ParseJsonText(text);
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, options?.SerializerOptions);
            return value is null
                ? throw new StructuredOutputException("JSON output deserialized to null.", text)
                : value;
        }
        catch (JsonException exception)
        {
            throw new StructuredOutputException(
                $"JSON output did not match the requested .NET type: {exception.Message}",
                text);
        }
    }

    public static string ParseJsonText(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            throw new StructuredOutputException("Expected JSON output, received an empty response.", text);
        }

        if (IsJson(trimmed))
        {
            return trimmed;
        }

        foreach (Match match in JsonFenceRegex().Matches(trimmed))
        {
            var candidate = match.Groups[1].Value.Trim();
            if (candidate.Length > 0 && IsJson(candidate))
            {
                return candidate;
            }
        }

        foreach (var candidate in FindJsonCandidates(trimmed))
        {
            if (IsJson(candidate))
            {
                return candidate;
            }
        }

        throw new StructuredOutputException("Expected valid JSON output from the agent.", text);
    }

    [GeneratedRegex("```(?:json)?\\s*([\\s\\S]*?)\\s*```", RegexOptions.IgnoreCase)]
    private static partial Regex JsonFenceRegex();

    private static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<string> FindJsonCandidates(string text)
    {
        var stack = new Stack<char>();
        var start = -1;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character is '{' or '[')
            {
                if (stack.Count == 0)
                {
                    start = i;
                }

                stack.Push(character);
                continue;
            }

            if (character is not ('}' or ']') || stack.Count == 0)
            {
                continue;
            }

            var opener = stack.Pop();
            var matches = opener == '{' && character == '}' ||
                opener == '[' && character == ']';
            if (!matches)
            {
                stack.Clear();
                start = -1;
                continue;
            }

            if (stack.Count == 0 && start >= 0)
            {
                yield return text[start..(i + 1)];
                start = -1;
            }
        }
    }
}

