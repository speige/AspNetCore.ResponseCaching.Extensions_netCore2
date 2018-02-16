using System.IO;
using System.Reflection;
using Microsoft.Extensions.Options;

namespace AspNetCore.ResponseCaching.Extensions.Implementations.DiskBackedMemory
{
  public class DiskBackedMemoryCacheOptions : IOptions<DiskBackedMemoryCacheOptions>
  {
    public string DiskCacheFolder { get; set; } = @"c:\temp\ResponseCache";
    public string VersionIdentifier { get; set; }

    public DiskBackedMemoryCacheOptions()
    {
      Assembly entryAssembly = Assembly.GetEntryAssembly();
      VersionIdentifier = entryAssembly.GetName().Version + "_" + File.GetLastWriteTime(entryAssembly.Location);
    }

    DiskBackedMemoryCacheOptions IOptions<DiskBackedMemoryCacheOptions>.Value
    {
      get
      {
        return this;
      }
    }
  }
}