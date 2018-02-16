using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCaching;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspNetCore.ResponseCaching.Extensions
{
  public class CustomResponseCachingMiddleware : ResponseCachingMiddleware
  {
    public CustomResponseCachingMiddleware(RequestDelegate next, IOptions<ResponseCachingOptions> responseCachingOptions, IOptions<CustomResponseCachingOptions> overridableResponseCachingOptions, ILoggerFactory loggerFactory, IResponseCachingPolicyProvider policyProvider, IResponseCachingKeyProvider keyProvider) : base(next, responseCachingOptions, loggerFactory, policyProvider, keyProvider)
    {
      FieldInfo cacheFieldInfo = typeof(ResponseCachingMiddleware).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance);
      cacheFieldInfo.SetValue(this, overridableResponseCachingOptions.Value.ResponseCacheFactory(responseCachingOptions));
    }
  }
}
