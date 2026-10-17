using System;
using System.IO;

namespace _855Media.Core.LDPlayer;

public static class PFlowMediaCaption
{
    public static string Resolve(string mediaFile, bool useTextFile)
    {
        if (useTextFile)
        {
            try
            {
                var textPath = Path.ChangeExtension(mediaFile, ".txt");
                if (File.Exists(textPath))
                {
                    var text = File.ReadAllText(textPath).Trim();
                    if (text.Length > 0)
                        return text;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return Path.GetFileNameWithoutExtension(mediaFile);
    }
}
