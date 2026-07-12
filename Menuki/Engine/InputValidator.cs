using System.Globalization;
using System.Text.RegularExpressions;
using Menuki.Config;

namespace Menuki.Engine;

/// <summary>Outcome of resolving/validating one input value.</summary>
public record InputResult(bool Ok, string Value, string? Error, string[]? Allowed = null);

/// <summary>
/// Typed-input core, shared by every consumer: the interactive prompt, the
/// headless runner and the authoring validator all agree on what a value must
/// look like, so a human, a script and an agent get the same contract.
/// </summary>
public static class InputValidator
{
    private const string DefaultDateFormat = "yyyy-MM-dd";

    public static string EffectiveType(InputDefinition input) =>
        string.IsNullOrWhiteSpace(input.Type) ? InputTypes.String : input.Type!.Trim().ToLowerInvariant();

    /// <summary>Whether a value must be supplied (no empty fallback is acceptable).</summary>
    public static bool IsRequired(InputDefinition input) => input.Required || input.Default is null;

    /// <summary>
    /// Resolve a value for one input: prefer <paramref name="supplied"/>, else the
    /// default, then validate it against the input's type. Returns a normalized value.
    /// </summary>
    public static InputResult Resolve(InputDefinition input, string? supplied)
    {
        var raw = !string.IsNullOrEmpty(supplied) ? supplied! : input.Default;

        if (string.IsNullOrEmpty(raw))
        {
            return IsRequired(input)
                ? new InputResult(false, "", $"Missing required parameter '{input.Name}'.")
                : new InputResult(true, "", null);
        }

        return ValidateValue(input, raw);
    }

    /// <summary>Validate a concrete non-empty value against the input's type; returns the normalized value.</summary>
    public static InputResult ValidateValue(InputDefinition input, string value)
    {
        var v = value.Trim();
        return EffectiveType(input) switch
        {
            InputTypes.Choice => ValidateChoice(input, v),
            InputTypes.Number => ValidateNumber(input, v),
            InputTypes.Boolean => ValidateBoolean(input, v),
            InputTypes.Date => ValidateDate(input, v),
            _ => ValidateString(input, v),
        };
    }

    private static InputResult ValidateString(InputDefinition input, string v)
    {
        if (!string.IsNullOrEmpty(input.Pattern) && !Regex.IsMatch(v, input.Pattern))
        {
            var hint = string.IsNullOrEmpty(input.Example) ? "" : $" (e.g. {input.Example})";
            return Fail(input, $"'{v}' does not match required format {input.Pattern}{hint}.");
        }
        return Ok(v);
    }

    private static InputResult ValidateChoice(InputDefinition input, string v)
    {
        var options = input.Options ?? new List<string>();
        if (options.Contains(v))
            return Ok(v);
        return new InputResult(false, "",
            $"Invalid value '{v}' for parameter '{input.Name}'.", options.ToArray());
    }

    private static InputResult ValidateNumber(InputDefinition input, string v)
    {
        if (!double.TryParse(v, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            return Fail(input, $"'{v}' is not a number.");
        if (input.Min is { } min && n < min)
            return Fail(input, $"{v} is below the minimum of {min}.");
        if (input.Max is { } max && n > max)
            return Fail(input, $"{v} is above the maximum of {max}.");
        return Ok(v);
    }

    private static readonly HashSet<string> Truthy = new(StringComparer.OrdinalIgnoreCase) { "true", "yes", "y", "1", "on" };
    private static readonly HashSet<string> Falsy = new(StringComparer.OrdinalIgnoreCase) { "false", "no", "n", "0", "off" };

    private static InputResult ValidateBoolean(InputDefinition input, string v)
    {
        if (Truthy.Contains(v)) return Ok("true");
        if (Falsy.Contains(v)) return Ok("false");
        return Fail(input, $"'{v}' is not a yes/no value (use true or false).");
    }

    private static InputResult ValidateDate(InputDefinition input, string v)
    {
        var fmt = string.IsNullOrEmpty(input.Format) ? DefaultDateFormat : input.Format!;
        if (DateTime.TryParseExact(v, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return Ok(dt.ToString(fmt, CultureInfo.InvariantCulture));
        return Fail(input, $"'{v}' is not a valid date (expected format {fmt}).");
    }

    /// <summary>A short hint appended to a prompt, e.g. " (1-100)" or " [staging, production]".</summary>
    public static string PromptHint(InputDefinition input)
    {
        switch (EffectiveType(input))
        {
            case InputTypes.Number:
                if (input.Min is { } lo && input.Max is { } hi) return $" ({Num(lo)}-{Num(hi)})";
                if (input.Min is { } l) return $" (>= {Num(l)})";
                if (input.Max is { } h) return $" (<= {Num(h)})";
                return "";
            case InputTypes.Boolean:
                return " (yes/no)";
            case InputTypes.Date:
                return $" ({(string.IsNullOrEmpty(input.Format) ? DefaultDateFormat : input.Format)})";
            case InputTypes.String when !string.IsNullOrEmpty(input.Example):
                return $" (e.g. {input.Example})";
            default:
                return "";
        }
    }

    /// <summary>Structural checks for authoring: catches a broken spec before it runs.</summary>
    public static IEnumerable<string> CheckSpec(InputDefinition input)
    {
        var type = EffectiveType(input);
        if (!InputTypes.All.Contains(type))
        {
            yield return $"input '{input.Name}' has unknown type '{input.Type}' (use {string.Join(", ", InputTypes.All)}).";
            yield break;
        }

        switch (type)
        {
            case InputTypes.Choice:
                if (input.Options == null || input.Options.Count == 0)
                    yield return $"choice input '{input.Name}' requires a non-empty 'options' list.";
                break;
            case InputTypes.Number:
                if (input.Min is { } min && input.Max is { } max && min > max)
                    yield return $"number input '{input.Name}' has min ({min}) greater than max ({max}).";
                break;
            case InputTypes.String:
                if (!string.IsNullOrEmpty(input.Pattern) && !IsValidRegex(input.Pattern))
                    yield return $"input '{input.Name}' has an invalid regex pattern.";
                break;
        }

        // A declared default must itself satisfy the type.
        if (input.Default is { } def && def.Length > 0)
        {
            var r = ValidateValue(input, def);
            if (!r.Ok)
                yield return $"input '{input.Name}' default {r.Error}";
        }
    }

    private static bool IsValidRegex(string pattern)
    {
        try { _ = Regex.Match("", pattern); return true; }
        catch (ArgumentException) { return false; }
    }

    private static string Num(double d) =>
        d == Math.Floor(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString(CultureInfo.InvariantCulture);

    private static InputResult Ok(string value) => new(true, value, null);
    private static InputResult Fail(InputDefinition input, string message) =>
        new(false, "", $"Invalid value for parameter '{input.Name}': {message}");
}
