namespace SemperSounds.Web;

/// <summary>
/// Decides whether a navigation destination is the one currently being viewed.
/// </summary>
/// <remarks>
/// Extracted from <c>MainLayout.razor</c> for the same reason <see cref="BoardView"/> is
/// extracted from <c>Home.razor</c>: there is no bUnit in this solution, so a rule left in
/// markup is a rule nothing can test. The matching is segment-aware rather than a plain
/// <c>StartsWith</c>, because <c>/entry-sound</c> is a string prefix of <c>/entry-sounds</c>
/// and a prefix test would light the wrong destination the day such a route is added.
/// </remarks>
public static class NavDestination
{
    /// <summary>Marks the active entry in both nav variants; styled in <c>app.css</c>.</summary>
    public const string ActiveClass = "ss-nav-active";

    /// <param name="relativePath">As returned by <c>NavigationManager.ToBaseRelativePath</c>, so no leading slash.</param>
    /// <param name="href">As written in the markup, so usually with one.</param>
    public static bool IsActive(string? relativePath, string href)
    {
        var path = Normalize(relativePath);
        var target = Normalize(href);

        // Root is a prefix of literally everything, so it only ever matches itself. It is not
        // in the nav today, but a Landing entry added later must not light up on every page.
        if (target.Length == 0) return path.Length == 0;

        if (!path.StartsWith(target, StringComparison.OrdinalIgnoreCase)) return false;

        // Either the whole path, or a parent of it at a segment boundary — the latter so a
        // child route keeps its parent lit.
        return path.Length == target.Length || path[target.Length] == '/';
    }

    /// <summary>For the History menu, whose activator has no href of its own.</summary>
    public static bool IsGroupActive(string? relativePath, params string[] hrefs) =>
        Array.Exists(hrefs, href => IsActive(relativePath, href));

    public static string? ClassFor(string? relativePath, string href) =>
        IsActive(relativePath, href) ? ActiveClass : null;

    public static string? ClassForGroup(string? relativePath, params string[] hrefs) =>
        IsGroupActive(relativePath, hrefs) ? ActiveClass : null;

    /// <remarks>Drops the query and fragment, and both surrounding slashes, so the two sides
    /// are comparable however each was written.</remarks>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var path = value.AsSpan().Trim();
        var cut = path.IndexOfAny('?', '#');
        if (cut >= 0) path = path[..cut];

        return path.Trim('/').ToString();
    }
}
