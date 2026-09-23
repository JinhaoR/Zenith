namespace Zenith.Core.Registry;

internal static class RegistryFields
{
    internal static string Text(string value, string field, int limit = 2048)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > limit || value.Any(char.IsControl))
            throw new ArgumentException($"{field} must be nonempty plain text of at most {limit} characters.");
        return value;
    }

    internal static string Id(string value, string field = "ID")
    {
        Text(value, field, 80);
        if (value[0] is < 'a' or > 'z' || value.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_'))
            throw new ArgumentException($"{field} must use lowercase snake_case and begin with a letter.");
        return value;
    }

    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string field)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.Take(1001).ToArray();
        if (copy.Length > 1000 || copy.Any(value => value is null))
            throw new ArgumentException($"{field} contains null entries or exceeds 1000 entries.");
        return Array.AsReadOnly(copy);
    }

    internal static IReadOnlyList<string> Ids(IEnumerable<string> values, string field)
    {
        var copy = Copy(values, field);
        foreach (var id in copy) Id(id, field);
        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Count)
            throw new ArgumentException($"{field} contains duplicate references.");
        return copy;
    }
}
