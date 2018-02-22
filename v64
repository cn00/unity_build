#!/usr/bin/env csharp -s

using System.IO;

static class F
{
    public static ulong GetFnv64(string path)
    {
        const ulong FNV_OFFSET_BASIS_64 = 14695981039346656037;
        const ulong FNV_PRIME_64 = 1099511628211;
        byte[] buf = File.ReadAllBytes(path);

        ulong hash = FNV_OFFSET_BASIS_64;
        for (int ii = 0; ii < buf.Length; ii++)
            hash = (FNV_PRIME_64 * hash) ^ (buf[ii]);

        return hash;
    }
}

foreach(var f in Args)
{
    if(File.Exists(f))
        Console.WriteLine(f + "\t" + F.GetFnv64(f));
}
