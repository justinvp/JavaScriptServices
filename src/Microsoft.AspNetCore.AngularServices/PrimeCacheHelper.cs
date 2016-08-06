using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.AngularServices
{
    public static class PrimeCacheHelper
    {
        public static async Task<IHtmlContent> PrimeCache(this IHtmlHelper html, string url)
        {
            // TODO: Consider deduplicating the PrimeCache calls (that is, if there are multiple requests to precache
            // the same URL, only return nonempty for one of them). This will make it easier to auto-prime-cache any
            // HTTP requests made during server-side rendering, without risking unnecessary duplicate requests.

            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("Value cannot be null or empty", nameof(url));
            }

            try
            {
                var request = html.ViewContext.HttpContext.Request;
                var baseUriString = string.Concat(
                    request.Scheme,
                    "://",
                    request.Host.ToUriComponent(),
                    request.PathBase.ToUriComponent(),
                    request.Path.ToUriComponent(),
                    request.QueryString.ToUriComponent());
                var fullUri = new Uri(new Uri(baseUriString), url);
                var response = await new HttpClient().GetAsync(fullUri.ToString()).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new PrimeCacheScript(url, response.StatusCode, responseBody);
            }
            catch (Exception ex)
            {
                var logger = (ILogger)html.ViewContext.HttpContext.RequestServices.GetService(typeof(ILogger));
                logger?.LogWarning("Error priming cache for URL: " + url, ex);
                return HtmlString.Empty;
            }
        }

        private sealed class PrimeCacheScript : IHtmlContent
        {
            private readonly string _url;
            private readonly HttpStatusCode _responseStatusCode;
            private readonly string _responseBody;

            public PrimeCacheScript(string url, HttpStatusCode responseStatusCode, string responseBody)
            {
                _url = url;
                _responseStatusCode = responseStatusCode;
                _responseBody = responseBody;
            }

            // These properties exist to be serialized as JSON without having to allocate an anonymous object.
            public HttpStatusCode statusCode => _responseStatusCode;
            public string body => _responseBody;

            public void WriteTo(TextWriter writer, HtmlEncoder encoder)
            {
                if (writer == null)
                {
                    throw new ArgumentNullException(nameof(writer));
                }

                if (encoder == null)
                {
                    throw new ArgumentNullException(nameof(encoder));
                }

                var serializer = new JsonSerializer();
                var jsonWriter = new JsonTextWriter(writer);
                jsonWriter.WriteRaw("<script>window.__preCachedResponses=window.__preCachedResponses||{},window.__preCachedResponses[");
                serializer.Serialize(jsonWriter, _url);
                jsonWriter.WriteRaw("]=");
                serializer.Serialize(jsonWriter, this);
                jsonWriter.WriteRaw(";</script>");
            }
        }
    }
}