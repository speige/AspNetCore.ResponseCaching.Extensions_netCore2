using System;
using Microsoft.AspNetCore.ResponseCaching;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.Options;

namespace AspNetCore.ResponseCaching.Extensions
{
  public class CustomResponseCachingOptions
  {
    public Func<IOptions<ResponseCachingOptions>, IResponseCache> ResponseCacheFactory { get; set; }
  }
}