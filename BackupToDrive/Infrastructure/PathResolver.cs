using Microsoft.Extensions.Hosting;

namespace BackupToDrive.Infrastructure;

public sealed class PathResolver
{
    private readonly IHostEnvironment _environment;

    public PathResolver(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var fromContentRoot = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, path));
        if (File.Exists(fromContentRoot) || Directory.Exists(fromContentRoot))
        {
            return fromContentRoot;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }
}
