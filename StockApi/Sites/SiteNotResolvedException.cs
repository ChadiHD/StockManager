using System;

namespace StockApi.Sites
{
    /// <summary>
    /// Thrown when a controller reads <see cref="IAdminSiteContext.Site"/> on a request that
    /// never named a store. <see cref="AdminSiteResolutionMiddleware"/> turns it into a 400,
    /// so the caller is told to send X-Site-Key rather than getting an opaque 500.
    /// </summary>
    public sealed class SiteNotResolvedException : Exception
    {
        public SiteNotResolvedException()
            : base("This request did not name a store. Send an X-Site-Key header naming an " +
                   "active site; GET /api/Site lists them.")
        {
        }
    }
}
