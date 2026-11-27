using System;
using System.IO;
using System.Linq;

namespace TerrariaPlayerAgent.Common;

internal static class RequestMailbox
{
    internal static string? Claim(string directory, string channel)
    {
        try
        {
            string? pending = Directory.EnumerateFiles(directory, channel + "-*.json").FirstOrDefault();
            if (pending == null) return null;
            string claimed = pending + ".claimed";
            // Each request has its own path. Moving it and the sender's timeout deletion compete for that path.
            File.Move(pending, claimed, overwrite: true);
            return claimed;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static void Release(string claimed)
    {
        try { File.Delete(claimed); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
