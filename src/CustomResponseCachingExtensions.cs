using Microsoft.AspNetCore.Builder;

namespace AspNetCore.ResponseCaching.Extensions
{
  public static class CustomResponseCachingExtensions
  {
    public static IApplicationBuilder UseCustomResponseCaching(this IApplicationBuilder app)
    {
      return app.UseMiddleware<CustomResponseCachingMiddleware>();
    }
  }
}