using System.Collections.Frozen;
using System.Reflection;

namespace RushDay.Infrastructure.Identity;

/// <summary>
/// Common breached passwords of twelve or more characters (01-domain-and-data.md section 8): they pass the length
/// rule yet are the first guesses in any credential-stuffing list. Loaded once from the embedded resource
/// <c>Identity/blocked-passwords.txt</c> (SecLists' top-1,000,000 list filtered to 12+ characters, in list order,
/// merged with the hand-written entries S1 shipped; one per line, lower-case). Compared case-insensitively.
/// </summary>
public static class BlockedPasswords
{
    public const string ResourceName = "RushDay.Infrastructure.Identity.blocked-passwords.txt";

    public static FrozenSet<string> Set { get; } = Load();

    private static FrozenSet<string> Load()
    {
        using var stream = typeof(BlockedPasswords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing from {typeof(BlockedPasswords).Assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);

        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            var entry = line.Trim();
            if (entry.Length > 0)
            {
                entries.Add(entry);
            }
        }

        return entries.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
