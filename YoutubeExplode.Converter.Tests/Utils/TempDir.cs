﻿using System;
using System.IO;
using System.Reflection;
using PathEx = System.IO.Path;

namespace YoutubeExplode.Converter.Tests.Utils;

internal partial class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string path) =>
        Path = path;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

internal partial class TempDir
{
    public static TempDir Create()
    {
        var dirPath = PathEx.Combine(
            PathEx.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory(),
            "Temp",
            Guid.NewGuid().ToString()
        );

        Directory.CreateDirectory(dirPath);

        return new TempDir(dirPath);
    }
}