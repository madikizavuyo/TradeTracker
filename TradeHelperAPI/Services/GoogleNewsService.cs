using System.Globalization;
using System.Xml.Linq;

namespace TradeHelper.Services
{
    /// <summary>Google News RSS (no API key). Used as first news source for TrailBlazer.</summary>
    public class GoogleNewsService
    {
        private static readonly SemaphoreSlim Throttle = new(1, 1);
        private static DateTime _lastCall = DateTime.MinValue;
        private const double MinSecondsBetweenCalls = 0.4;

        private readonly HttpClient _client;
        private readonly ILogger<GoogleNewsService> _logger;

        public GoogleNewsService(HttpClient client, ILogger<GoogleNewsService> logger)
        {
            _client = client;
            _logger = logger;
            _client.Timeout = TimeSpan.FromSeconds(25);
            if (_client.DefaultRequestHeaders.UserAgent.Count == 0)
                _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (compatible; TradeHelper/1.0; +https://github.com/)");
        }

        public async Task<List<NewsItem>> FetchNewsAsync(string symbol, string? assetClass, int maxItems = 12, CancellationToken cancellationToken = default)
        {
            var items = new List<NewsItem>();
            var q = BuildQuery(symbol, assetClass);
            if (string.IsNullOrEmpty(q)) return items;

            var url = "https://news.google.com/rss/search?q=" + Uri.EscapeDataString(q) + "&hl=en-US&gl=US&ceid=US:en";

            await ThrottleAsync(cancellationToken);
            string xml;
            try
            {
                xml = await _client.GetStringAsync(new Uri(url), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Google News RSS failed for {Symbol}", symbol);
                return items;
            }

            try
            {
                var doc = XDocument.Parse(xml);
                XNamespace ns = "http://www.w3.org/2005/Atom";
                IEnumerable<XElement> elements;
                bool atom = string.Equals(doc.Root?.Name.LocalName, "feed", StringComparison.OrdinalIgnoreCase);
                if (atom)
                    elements = doc.Root!.Elements(ns + "entry");
                else
                    elements = doc.Root?.Element("channel")?.Elements("item") ?? Enumerable.Empty<XElement>();

                foreach (var el in elements)
                {
                    if (items.Count >= maxItems) break;

                    string title, link, desc, pub;
                    if (atom)
                    {
                        title = (string?)el.Element(ns + "title") ?? "";
                        link = (string?)el.Element(ns + "link")?.Attribute("href") ?? "";
                        desc = StripHtml((string?)el.Element(ns + "content") ?? (string?)el.Element(ns + "summary") ?? "");
                        pub = (string?)el.Element(ns + "published") ?? (string?)el.Element(ns + "updated") ?? "";
                    }
                    else
                    {
                        title = (string?)el.Element("title") ?? "";
                        link = (string?)el.Element("link") ?? "";
                        desc = StripHtml((string?)el.Element("description") ?? "");
                        pub = (string?)el.Element("pubDate") ?? "";
                    }

                    if (string.IsNullOrWhiteSpace(title)) continue;

                    DateTime published = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(pub))
                    {
                        if (DateTime.TryParse(pub, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                            published = dt.ToUniversalTime();
                        else if (DateTime.TryParse(pub, out dt))
                            published = dt.ToUniversalTime();
                    }

                    items.Add(new NewsItem
                    {
                        Headline = title.Trim(),
                        Summary = desc.Trim(),
                        Source = "Google News",
                        Url = link.Trim(),
                        ImageUrl = "",
                        PublishedAt = published
                    });
                }

                if (items.Count > 0)
                    _logger.LogDebug("Google News: {Count} items for {Symbol}", items.Count, symbol);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Google News RSS parse failed for {Symbol}", symbol);
            }

            return items;
        }

        private static string BuildQuery(string symbol, string? assetClass)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return "";
            var s = symbol.Trim().ToUpperInvariant().Replace("/", "").Replace("_", "");
            if (s.Length == 6 && s.All(char.IsLetter))
            {
                var b = s[..3];
                var q = s[3..];
                return $"{b}/{q} forex OR {b} {q} currency OR FX {b}{q}";
            }
            if (s.Contains("XAU", StringComparison.Ordinal) || s.Contains("GOLD", StringComparison.Ordinal))
                return "gold price XAU forex OR spot gold";
            if (s.Contains("XAG", StringComparison.Ordinal) || s.Contains("SILVER", StringComparison.Ordinal))
                return "silver price XAG forex OR spot silver";
            if (s == "USOIL" || s == "WTI" || s == "CL")
                return "WTI crude oil price OR oil futures";
            if (s == "US500" || s == "SPX")
                return "S&P 500 stock market OR SPX index";
            return $"{symbol} forex OR {symbol} market";
        }

        private static string StripHtml(string? html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var s = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
            return System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
        }

        private static async Task ThrottleAsync(CancellationToken cancellationToken)
        {
            await Throttle.WaitAsync(cancellationToken);
            try
            {
                var elapsed = (DateTime.UtcNow - _lastCall).TotalSeconds;
                if (elapsed < MinSecondsBetweenCalls)
                    await Task.Delay(TimeSpan.FromMilliseconds((int)((MinSecondsBetweenCalls - elapsed) * 1000)), cancellationToken);
                _lastCall = DateTime.UtcNow;
            }
            finally { Throttle.Release(); }
        }
    }
}
