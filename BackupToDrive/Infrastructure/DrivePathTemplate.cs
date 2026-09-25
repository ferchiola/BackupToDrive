namespace BackupToDrive.Infrastructure;

public static class DrivePathTemplate
{
    public static string Render(string template, string jobName, DateTime at)
    {
        var rendered = template
            .Replace("{JobName}", Sanitize(jobName), StringComparison.OrdinalIgnoreCase)
            .Replace("{yyyy}", at.ToString("yyyy"), StringComparison.OrdinalIgnoreCase)
            .Replace("{MM}", at.ToString("MM"), StringComparison.OrdinalIgnoreCase)
            .Replace("{dd}", at.ToString("dd"), StringComparison.OrdinalIgnoreCase);

        return string.Join(
            '/',
            rendered.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name.Trim();
    }
}
