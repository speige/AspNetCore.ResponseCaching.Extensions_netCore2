using System;
using AspNetCore.ResponseCaching.Extensions.Implementations.DiskBackedMemory;
using Microsoft.AspNetCore.ResponseCaching;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AspNetCore.ResponseCaching.Extensions
{
  public static class ServiceCollectionExtensions
  {
    public static void AddDiskBackedMemoryResponseCaching(this IServiceCollection services, Action<ResponseCachingOptions, DiskBackedMemoryCacheOptions> configureOptions = null, bool ignoreBrowserNoCacheNoStore = true)
    {
      DiskBackedMemoryCacheOptions diskBackedMemoryCacheOptions = new DiskBackedMemoryCacheOptions();
      if (configureOptions != null)
      {
        services.Configure<ResponseCachingOptions>(x =>
        {
          configureOptions(x, diskBackedMemoryCacheOptions);
        });
      }

      services.Configure<CustomResponseCachingOptions>(x =>
      {
        x.ResponseCacheFactory = responseCachingOptions => new DiskBackedMemoryCache(responseCachingOptions.Value.SizeLimit, responseCachingOptions.Value.MaximumBodySize, diskBackedMemoryCacheOptions);
      });

      if (ignoreBrowserNoCacheNoStore)
      {
        services.AddResponseCaching<CustomResponseCachingMiddleware, IgnoreBrowserNoCacheNoStoreResponseCachingPolicyProvider, ResponseCachingKeyProvider>();
      }
      else
      {
        services.AddResponseCaching<CustomResponseCachingMiddleware, ResponseCachingPolicyProvider, ResponseCachingKeyProvider>();
      }
    }

    public static void AddResponseCaching<T1, T2, T3>(this IServiceCollection services, Action<ResponseCachingOptions> configureOptions = null) where T1 : ResponseCachingMiddleware where T2 : class, IResponseCachingPolicyProvider where T3 : class, IResponseCachingKeyProvider
    {
      if (configureOptions != null)
      {
        services.Configure(configureOptions);
      }

      if (typeof(T1) == typeof(ResponseCachingMiddleware))
      {
        services.AddMemoryCache();
      }
      services.TryAdd(ServiceDescriptor.Singleton<IResponseCachingPolicyProvider, T2>());
      services.TryAdd(ServiceDescriptor.Singleton<IResponseCachingKeyProvider, T3>());
    }
  }
}