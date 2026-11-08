using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace VmixSubtitleController;
// Shared disk cache. Nothing is loaded into memory at application startup.
internal static class TranslationCache
{
    private static readonly object Sync = new();
    internal static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "im-too-lazy", "translation-cache");
    private static int _generation;
    private static int _writes;
    internal static int Generation { get { lock(Sync) return _generation; } }
    internal static string Key(object context) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(context))));
    internal static string? Read(string key)
    {
        lock(Sync) try
        {
            var path=Path.Combine(Root,key+".json");
            if(!File.Exists(path) || new FileInfo(path).Length>100_000) return null;
            var value=JsonSerializer.Deserialize<string>(File.ReadAllText(path));
            if(string.IsNullOrWhiteSpace(value))return null;
            File.SetLastWriteTimeUtc(path,DateTime.UtcNow);
            return value;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    internal static void Write(string key,string value,int generation)
    {
        lock(Sync) try
        {
            if(generation!=_generation || string.IsNullOrWhiteSpace(value) || value.Length>20_000)return;
            Directory.CreateDirectory(Root);
            var path=Path.Combine(Root,key+".json");
            var temp=path+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(value));File.Move(temp,path,true);
            if(++_writes%64==1)
            {
                var files=new DirectoryInfo(Root).GetFiles("*.json").OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();
                long bytes=0;
                for(int i=0;i<files.Length;i++) { bytes+=files[i].Length; if(i>=10_000 || bytes>50L*1024*1024)files[i].Delete(); }
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { /* Cache failure must not interrupt translation. */ }
    }
    internal static object Stats()
    {
        lock(Sync)
        {
            var files=Directory.Exists(Root)?new DirectoryInfo(Root).GetFiles("*.json"):Array.Empty<FileInfo>();
            return new { count=files.Length,bytes=files.Sum(f=>f.Length) };
        }
    }
    internal static void Clear()
    {
        lock(Sync)
        {
            ++_generation;
            if(Directory.Exists(Root)) foreach(var file in Directory.EnumerateFiles(Root))File.Delete(file);
        }
    }
}
