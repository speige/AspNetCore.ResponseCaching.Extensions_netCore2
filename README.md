# AspNetCore.ResponseCaching.Extensions

## Description
Extensions for overriding the behavior of the default ASP.Net Core ResponseCaching as well as additional Cache implementations such as Disk. (Similar to OutputCache of previous versions of ASP.Net)

## License
MIT

## Contributing
Pull requests are welcome

## NuGet
https://www.nuget.org/packages/AspNetCore.ResponseCaching.Extensions/

# Cache Implementations
These inherit from the base ResponseCache so they follow the same basic rules.
For example: A response must have a cache-control of public with a future max-age in order to be cached.

For more info see https://github.com/aspnet/ResponseCaching and https://docs.microsoft.com/en-us/aspnet/core/performance/caching/middleware

However, they allow for extra customizations.
Example: The default ResponseCache complies with the browser's no-cache/no-store HTTP Request Headers. This makes sense for an intermediary cache like an ISP or CDN, but not for the originating server.
This behavior can be overridden via this extension method:
```
namespace AspNetCore.ResponseCaching.Extensions
{
	public static void AddResponseCaching(this IServiceCollection services, Action<ResponseCachingOptions> configureOptions = null, bool ignoreBrowserNoCacheNoStore = true)
}
```


## DiskBackedMemoryCache
High-performance due to limited use of locks. Allows infinite cache size on disk while still limiting the amount in RAM (retention based on most recently requested URLs). Doesn't lose response cache during app/server restarts. Auto clears cache when new versions of your app are deployed. 

### Usage
Add this to the top of Startup.cs
```
	using AspNetCore.ResponseCaching.Extensions;
```

Add this to the ConfigureServices method of Startup.cs
```
	services.AddDiskBackedMemoryResponseCaching((x, y) =>
	{
		x.MaximumBodySize = 5 * 1024 * 1024; // Default of 64MB is probably way too big for most scenarios
	});
```

Add this to the Configure method of Startup.cs
```
	app.UseCustomResponseCaching();
```
