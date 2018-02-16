using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace AspNetCore.ResponseCaching.Extensions.Implementations.DiskBackedMemory
{
  public class DiskBackedMemoryCache : IResponseCache
  {
    //note: For performance reasons this code avoids locking where possible. It has been carefully coded so that concurrency will never cause bugs (incorrect/stale responses), but rarely it will create inconsequential side-effects (temporarily allow more than maxMemoryBytes to be used or return a cache-miss that should have been a hit)

    private long _esimatedUsedMemoryBytes = 0; // since we don't lock when modifying this, it won't be 100% accurate when two threads add a new item to the cache at the exact same time.
    private readonly long _maxMemoryBytes;
    private readonly int _maxResponseBodyBytes;
    private readonly string _diskCacheFolder;
    private readonly string _versionIdentifier;
    private Dictionary<string, CacheEntry> _memoryCache = new Dictionary<string, CacheEntry>();

    public DiskBackedMemoryCache(long maxMemoryBytes, long maxResponseBodyBytes, IOptions<DiskBackedMemoryCacheOptions> options)
    {
      _maxMemoryBytes = maxMemoryBytes;
      _maxResponseBodyBytes = (int)maxResponseBodyBytes;
      _diskCacheFolder = options.Value.DiskCacheFolder;
      _versionIdentifier = options.Value.VersionIdentifier;
      Directory.CreateDirectory(_diskCacheFolder);
    }

    private static string Base32Encode(string key)
    {
      return string.Join("", Encoding.UTF8.GetBytes(key).Select(b => "abcdefghijklmonpqrstuvwxyz234567"[b & 0x1F]));
    }

    public string GetFileName(string key)
    {
      return Path.Combine(_diskCacheFolder, Base32Encode(key));
    }

    [Serializable]
    private class CacheEntry
    {
      public string ServerVersion { get; set; }
      public DateTimeOffset LastAccessed { get; set; }
      public DateTimeOffset Created { get; set; }
      public TimeSpan ValidFor { get; set; }

      public static CacheEntry Deserialize(byte[] bytes)
      {
        BinaryFormatter binaryFormatter = new BinaryFormatter();
        using (MemoryStream stream = new MemoryStream(bytes))
        {
          return (CacheEntry)binaryFormatter.Deserialize(stream);
        }
      }

      public byte[] Serialize()
      {
        BinaryFormatter binaryFormatter = new BinaryFormatter();
        using (MemoryStream stream = new MemoryStream())
        {
          binaryFormatter.Serialize(stream, this);
          return stream.ToArray();
        }
      }
    }

    [Serializable]
    private class VaryByKeysCacheEntry : CacheEntry
    {
      public string[] Headers { get; set; }
      public string[] QueryKeys { get; set; }
      public string VaryByKeyPrefix { get; set; }
    }

    [Serializable]
    private class ResponseCacheEntry : CacheEntry
    {
      public int StatusCode { get; set; }
      public Dictionary<string, string[]> HeaderDictionary { get; set; }
      public byte[] Body { get; set; }
    }

    public IResponseCacheEntry Get(string key)
    {
      try
      {
        CacheEntry cacheEntry = GetCacheEntry(key);

        if (cacheEntry is VaryByKeysCacheEntry)
        {
          VaryByKeysCacheEntry varyByKeysCacheEntry = (VaryByKeysCacheEntry)cacheEntry;
          return new CachedVaryByRules()
          {
            Headers = new StringValues(varyByKeysCacheEntry.Headers),
            QueryKeys = new StringValues(varyByKeysCacheEntry.QueryKeys),
            VaryByKeyPrefix = varyByKeysCacheEntry.VaryByKeyPrefix
          };
        }

        if (cacheEntry is ResponseCacheEntry)
        {
          ResponseCacheEntry responseCacheEntry = (ResponseCacheEntry)cacheEntry;
          return new CachedResponse()
          {
            Body = new MemoryStream(responseCacheEntry.Body),
            Headers = new HeaderDictionary(responseCacheEntry.HeaderDictionary.ToDictionary(x => x.Key, x => new StringValues(x.Value))),
            StatusCode = responseCacheEntry.StatusCode,
            Created = responseCacheEntry.Created
          };
        }
      }
      catch
      {
      }

      return null;
    }

    private CacheEntry GetCacheEntry(string key)
    {
      try
      {
        _memoryCache.TryGetValue(key, out CacheEntry cacheEntry);
        string fileName = null;

        if (cacheEntry == null)
        {
          fileName = GetFileName(key);
          if (!File.Exists(fileName))
          {
            return null;
          }

          byte[] file = File.ReadAllBytes(fileName);
          cacheEntry = CacheEntry.Deserialize(file);
          _memoryCache[key] = cacheEntry;
          _esimatedUsedMemoryBytes += (cacheEntry as ResponseCacheEntry)?.Body?.Length ?? 0;
          PurgeExcessMemoryAsync();
        }

        if (cacheEntry.Created.Add(cacheEntry.ValidFor) < DateTime.UtcNow || cacheEntry.ServerVersion != _versionIdentifier)
        {
          _memoryCache[key] = null;
          _esimatedUsedMemoryBytes -= (cacheEntry as ResponseCacheEntry)?.Body?.Length ?? 0;
          if (fileName != null)
          {
            File.Delete(fileName);
          }

          return null;
        }

        cacheEntry.LastAccessed = DateTime.UtcNow;
        return cacheEntry;
      }
      catch
      {
      }

      return null;
    }

    private CacheEntry ConvertToCacheEntry(IResponseCacheEntry entry, TimeSpan validFor)
    {
      if (entry is CachedResponse)
      {
        CachedResponse cachedResponse = (CachedResponse)entry;

        int responseLengthInBytes = (int)cachedResponse.Body.Length;
        if (responseLengthInBytes > _maxResponseBodyBytes)
        {
          return null;
        }

        long oldPosition = cachedResponse.Body.Position;
        cachedResponse.Body.Position = 0;
        byte[] body = new byte[responseLengthInBytes];
        cachedResponse.Body.Read(body, 0, responseLengthInBytes);
        cachedResponse.Body.Position = oldPosition;

        return new ResponseCacheEntry()
        {
          ServerVersion = _versionIdentifier,
          Body = body,
          HeaderDictionary = cachedResponse.Headers.ToDictionary(x => x.Key, x => x.Value.ToArray()),
          StatusCode = cachedResponse.StatusCode,
          Created = cachedResponse.Created,
          ValidFor = validFor
        };
      }

      if (entry is CachedVaryByRules)
      {
        CachedVaryByRules cachedResponse = (CachedVaryByRules)entry;
        return new VaryByKeysCacheEntry()
        {
          ServerVersion = _versionIdentifier,
          Headers = cachedResponse.Headers,
          QueryKeys = cachedResponse.QueryKeys,
          VaryByKeyPrefix = cachedResponse.VaryByKeyPrefix,
          Created = DateTime.UtcNow,
          ValidFor = validFor
        };
      }

      return null;
    }

    private object _purgeLock = new object();
    private void PurgeExcessMemoryAsync()
    {
      if (_esimatedUsedMemoryBytes < _maxMemoryBytes)
      {
        return;
      }

      Task.Run(() =>
      {
        if (_esimatedUsedMemoryBytes < _maxMemoryBytes)
        {
          return;
        }

        lock (_purgeLock)
        {
          if (_esimatedUsedMemoryBytes < _maxMemoryBytes)
          {
            return;
          }

          long actualUsedMemoryBytes = _memoryCache.Values.OfType<ResponseCacheEntry>().Sum(x => (long)(x.Body?.Length ?? 0));
          long bytesToKeep = _maxMemoryBytes / 2; // drop to 50% of max capacity to prevent "thrashing"
          List<KeyValuePair<string, ResponseCacheEntry>> leastRecentlyUsedOrder = _memoryCache.OrderBy(x => x.Value.LastAccessed).Where(x => x.Value is ResponseCacheEntry).Select(x => new KeyValuePair<string, ResponseCacheEntry>(x.Key, (ResponseCacheEntry)x.Value)).ToList();
          while (actualUsedMemoryBytes > bytesToKeep)
          {
            if (leastRecentlyUsedOrder.Count == 0)
            {
              return;
            }

            KeyValuePair<string, ResponseCacheEntry> cacheEntry = leastRecentlyUsedOrder.First();
            leastRecentlyUsedOrder.Remove(cacheEntry);
            _memoryCache[cacheEntry.Key] = null;
            actualUsedMemoryBytes -= cacheEntry.Value.Body?.Length ?? 0;
          }
        }
      });
    }

    private void SaveToDiskAsync(string key, CacheEntry cacheEntry)
    {
      Task.Run(() =>
      {
        try
        {
          byte[] bytes = cacheEntry.Serialize();
          string fileName = GetFileName(key);
          File.WriteAllBytes(fileName, bytes);
        }
        catch
        {
        }
      });
    }

    public void Set(string key, IResponseCacheEntry entry, TimeSpan validFor)
    {
      try
      {
        CacheEntry cacheEntry = ConvertToCacheEntry(entry, validFor);
        if (cacheEntry != null)
        {
          _memoryCache.TryGetValue(key, out CacheEntry oldCacheEntry);
          _esimatedUsedMemoryBytes -= (oldCacheEntry as ResponseCacheEntry)?.Body?.Length ?? 0;
          _memoryCache[key] = cacheEntry;
          _esimatedUsedMemoryBytes += (cacheEntry as ResponseCacheEntry)?.Body?.Length ?? 0;
          SaveToDiskAsync(key, cacheEntry);
          PurgeExcessMemoryAsync();
        }
      }
      catch
      {
      }
    }

    public Task<IResponseCacheEntry> GetAsync(string key)
    {
      IResponseCacheEntry result = Get(key);
      return Task.FromResult(result);
    }

    public Task SetAsync(string key, IResponseCacheEntry entry, TimeSpan validFor)
    {
      return Task.Run(() =>
      {
        Set(key, entry, validFor);
      });
    }
  }
}