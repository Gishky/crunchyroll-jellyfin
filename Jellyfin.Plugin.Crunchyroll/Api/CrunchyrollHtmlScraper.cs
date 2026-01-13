using System.Text.RegularExpressions;
using System.Web;
using Jellyfin.Plugin.Crunchyroll.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Crunchyroll.Api;

/// <summary>
/// Scrapes Crunchyroll HTML pages to extract metadata.
/// Used when API access is blocked by Cloudflare.
/// </summary>
public static partial class CrunchyrollHtmlScraper
{
    /// <summary>
    /// Extracts series information from a Crunchyroll series page HTML.
    /// </summary>
    /// <param name="html">The HTML content of the series page.</param>
    /// <param name="seriesId">The Crunchyroll series ID.</param>
    /// <param name="logger">Logger for debugging.</param>
    /// <returns>The extracted series information or null if parsing failed.</returns>
    public static CrunchyrollSeries? ExtractSeriesFromHtml(string html, string seriesId, ILogger logger)
    {
        try
        {
            var series = new CrunchyrollSeries
            {
                Id = seriesId
            };

            // Extract title from h1
            var titleMatch = TitleRegex().Match(html);
            if (titleMatch.Success)
            {
                series.Title = HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
            }
            else
            {
                // Try og:title meta tag
                var ogTitleMatch = OgTitleRegex().Match(html);
                if (ogTitleMatch.Success)
                {
                    series.Title = HttpUtility.HtmlDecode(ogTitleMatch.Groups[1].Value.Trim());
                    // Remove " - Watch on Crunchyroll" suffix if present
                    series.Title = series.Title.Replace(" - Watch on Crunchyroll", "")
                                              .Replace(" - Crunchyroll", "");
                }
            }

            // Extract description
            var descMatch = DescriptionRegex().Match(html);
            if (descMatch.Success)
            {
                series.Description = HttpUtility.HtmlDecode(descMatch.Groups[1].Value.Trim());
            }
            else
            {
                // Try og:description meta tag
                var ogDescMatch = OgDescriptionRegex().Match(html);
                if (ogDescMatch.Success)
                {
                    series.Description = HttpUtility.HtmlDecode(ogDescMatch.Groups[1].Value.Trim());
                }
            }

            // Extract poster image
            var posterMatch = PosterImageRegex().Match(html);
            if (posterMatch.Success)
            {
                var imageUrl = posterMatch.Groups[1].Value;
                series.Images = new CrunchyrollImages
                {
                    PosterTall = new List<List<CrunchyrollImage>>
                    {
                        new List<CrunchyrollImage>
                        {
                            new CrunchyrollImage
                            {
                                Source = imageUrl,
                                Width = 480,
                                Height = 720,
                                Type = "poster_tall"
                            }
                        }
                    }
                };
            }

            // Extract slug title from URL if available
            var slugMatch = SlugTitleRegex().Match(html);
            if (slugMatch.Success)
            {
                series.SlugTitle = slugMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(series.Title))
            {
                logger.LogWarning("Failed to extract series title from HTML");
                return null;
            }

            logger.LogDebug("Extracted series from HTML: {Title}", series.Title);
            return series;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting series from HTML");
            return null;
        }
    }

    /// <summary>
    /// Extracts episode information from a Crunchyroll series page HTML.
    /// </summary>
    /// <param name="html">The HTML content of the series page.</param>
    /// <param name="logger">Logger for debugging.</param>
    /// <returns>List of extracted episodes.</returns>
    public static List<CrunchyrollEpisode> ExtractEpisodesFromHtml(string html, ILogger logger)
    {
        var episodes = new List<CrunchyrollEpisode>();

        // Debug: Check for JSON state which contains all data cleanly
        // If this log shows up, we can switch to JSON parsing in the future
        if (html.Contains("__INITIAL_STATE__") || html.Contains("__NEXT_DATA__"))
        {
             logger.LogInformation("[HTML Scraper] Detected embedded JSON State in HTML. Future versions can extract ALL seasons from here.");
        }

        try
        {
            // Match episode cards - they have data-t="episode-card"
            var episodeMatches = EpisodeCardRegex().Matches(html);

            foreach (Match match in episodeMatches)
            {
                var cardHtml = match.Value;
                var episode = new CrunchyrollEpisode();
                // 1. Extract raw data reliably from the visible title link
                // This element is always rendered, unlike the hover component
                var linkMatch = Regex.Match(cardHtml, @"playable-card__title-link""[^>]+href=""(?<url>[^""]+)""[^>]*>(?<fullTitle>[^<]+)</a>", RegexOptions.IgnoreCase);
                
                if (!linkMatch.Success) 
                {
                    logger.LogWarning("[HTML Scraper] Could not find title link in card");
                    continue;
                }

                string fullTitle = HttpUtility.HtmlDecode(linkMatch.Groups["fullTitle"].Value.Trim());
                string episodeUrl = linkMatch.Groups["url"].Value;

                // 2. Parse Metadata from Title (Supports: "T2 E13 - Title", "E1 - Title", "101 - Title")
                // Groups: 1=Season(optional), 2=Episode, 3=Title
                var metaMatch = Regex.Match(fullTitle, @"^(?:T(?<season>\d+)\s+)?(?:E(?<episode>\d+)\s*[-:]\s*)?(?<title>.+)$", RegexOptions.IgnoreCase);

                if (metaMatch.Success)
                {
                    episode.Title = metaMatch.Groups["title"].Value.Trim();
                    
                    // If episode number is found in title string
                    if (metaMatch.Groups["episode"].Success)
                    {
                        episode.EpisodeNumber = metaMatch.Groups["episode"].Value;
                    }
                    
                    // If season number is found (useful for future multi-season support)
                    if (metaMatch.Groups["season"].Success)
                    {
                        // Note: We currently prefer the detected season from the page, but this could be useful validation
                        logger.LogDebug("[HTML Scraper] Detected Season {S} in title: {T}", metaMatch.Groups["season"].Value, fullTitle);
                    }
                }
                else
                {
                    // Fallback: Use the whole string as title if regex fails completely
                    episode.Title = fullTitle;
                    logger.LogDebug("[HTML Scraper] Could not parse metadata from title: '{Title}'", fullTitle);
                }

                // 3. Extract Episode Number from URL if missing from title (Last resort)
                // URLs often look like /watch/GZ7UDM1KQ/episode-title
                if (string.IsNullOrEmpty(episode.EpisodeNumber))
                {
                    // Sometimes aria-label fallback helps
                     var ariaMatch = Regex.Match(cardHtml, @"aria-label=""Reproduzir[^""]*?Epis[oó]dio\s+(?<num>\d+)", RegexOptions.IgnoreCase);
                     if (ariaMatch.Success)
                     {
                         episode.EpisodeNumber = ariaMatch.Groups["num"].Value;
                     }
                }

                // 4. Final Processing
                if (int.TryParse(episode.EpisodeNumber, out int epNum))
                {
                    episode.EpisodeNumberInt = epNum;
                    episode.SequenceNumber = epNum;
                }

                if (string.IsNullOrEmpty(episode.Id))
                {
                    // Extract ID from URL: /watch/ID/slug
                    var urlParts = episodeUrl.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (urlParts.Length >= 2 && urlParts.Contains("watch"))
                    {
                        int watchIndex = Array.IndexOf(urlParts, "watch");
                        if (watchIndex + 1 < urlParts.Length)
                        {
                            episode.Id = urlParts[watchIndex + 1];
                        }
                    }
                }

                // Extract episode description
                var descMatch = EpisodeDescriptionRegex().Match(cardHtml);
                if (descMatch.Success)
                {
                    episode.Description = HttpUtility.HtmlDecode(descMatch.Groups[1].Value.Trim());
                }

                // Extract thumbnail image
                var thumbMatch = EpisodeThumbnailRegex().Match(cardHtml);
                if (thumbMatch.Success)
                {
                    var thumbnailUrl = thumbMatch.Groups[1].Value;
                    
                    // Upgrade thumbnail quality to 1080p
                    // Original: .../fit=contain,format=auto,quality=70,width=320,height=180/...
                    // Target: .../fit=contain,format=auto,quality=85,width=1920,height=1080/...
                    if (thumbnailUrl.Contains("width=") && thumbnailUrl.Contains("height="))
                    {
                        thumbnailUrl = thumbnailUrl
                            .Replace("width=320", "width=1920")
                            .Replace("height=180", "height=1080")
                            .Replace("quality=70", "quality=85");
                    }

                    episode.Images = new CrunchyrollEpisodeImages
                    {
                        Thumbnail = new List<List<CrunchyrollImage>>
                        {
                            new List<CrunchyrollImage>
                            {
                                new CrunchyrollImage
                                {
                                    Source = thumbnailUrl,
                                    Width = 1920,
                                    Height = 1080,
                                    Type = "thumbnail"
                                }
                            }
                        }
                    };
                }

                // Extract duration
                var durationMatch = EpisodeDurationRegex().Match(cardHtml);
                if (durationMatch.Success)
                {
                    var durationStr = durationMatch.Groups[1].Value;
                    if (int.TryParse(durationStr.Replace("m", ""), out int minutes))
                    {
                        episode.DurationMs = minutes * 60 * 1000;
                    }
                }

                if (!string.IsNullOrEmpty(episode.Id))
                {
                    episodes.Add(episode);
                }
            }

            // Log which episodes were found for debugging season issues
            if (episodes.Count > 0)
            {
                var episodeNumbers = string.Join(", ", episodes.Select(e => $"E{e.EpisodeNumber ?? "?"}"));
                var firstEp = episodes.First();
                var lastEp = episodes.Last();
                logger.LogInformation("[HTML Scraper] Extracted {Count} episodes: {EpisodeList}", episodes.Count, episodeNumbers);
                logger.LogInformation("[HTML Scraper] Episode range: E{First} to E{Last}", firstEp.EpisodeNumber ?? "?", lastEp.EpisodeNumber ?? "?");
            }
            else
            {
                logger.LogWarning("[HTML Scraper] No episodes found in HTML. Looking for 'episode-card' elements.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting episodes from HTML");
        }

        return episodes;
    }

    /// <summary>
    /// Extracts search results from Crunchyroll search page HTML.
    /// </summary>
    /// <param name="html">The HTML content.</param>
    /// <param name="logger">Logger for debugging.</param>
    /// <returns>List of search items.</returns>
    public static List<CrunchyrollSearchItem> ExtractSearchResultsFromHtml(string html, ILogger logger)
    {
        var results = new List<CrunchyrollSearchItem>();

        try
        {
            // Match series cards in search results
            var cardMatches = SeriesCardRegex().Matches(html);

            foreach (Match match in cardMatches)
            {
                var cardHtml = match.Value;
                var item = new CrunchyrollSearchItem();

                // Extract series ID from link
                var linkMatch = SeriesLinkRegex().Match(cardHtml);
                if (linkMatch.Success)
                {
                    item.Id = linkMatch.Groups[1].Value;
                    item.SlugTitle = linkMatch.Groups[2].Value;
                }

                // Extract title
                var titleMatch = SeriesTitleInCardRegex().Match(cardHtml);
                if (titleMatch.Success)
                {
                    item.Title = HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                }

                // Extract poster image
                var imageMatch = SeriesImageInCardRegex().Match(cardHtml);
                if (imageMatch.Success)
                {
                    item.Images = new CrunchyrollImages
                    {
                        PosterTall = new List<List<CrunchyrollImage>>
                        {
                            new List<CrunchyrollImage>
                            {
                                new CrunchyrollImage
                                {
                                    Source = imageMatch.Groups[1].Value,
                                    Width = 480,
                                    Height = 720,
                                    Type = "poster_tall"
                                }
                            }
                        }
                    };
                }

                if (!string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Title))
                {
                    results.Add(item);
                }
            }

            logger.LogDebug("Extracted {Count} search results from HTML", results.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting search results from HTML");
        }

        return results;
    }

    // Regex patterns for extracting data
    [GeneratedRegex(@"<h1[^>]*class=""[^""]*heading[^""]*""[^>]*>([^<]+)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<meta[^>]*property=""og:title""[^>]*content=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex OgTitleRegex();

    [GeneratedRegex(@"<p[^>]*class=""[^""]*text--gq6o-[^""]*text--is-l[^""]*""[^>]*>([^<]+)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DescriptionRegex();

    [GeneratedRegex(@"<meta[^>]*property=""og:description""[^>]*content=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex OgDescriptionRegex();

    [GeneratedRegex(@"<meta[^>]*property=""og:image""[^>]*content=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PosterImageRegex();

    [GeneratedRegex(@"/series/[A-Z0-9]+/([a-z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SlugTitleRegex();

    [GeneratedRegex(@"<div[^>]*class=""[^""]*playable-card[^""]*""[^>]*data-t=""episode-card[^""]*""[^>]*>.*?</div>\s*</div>\s*</div>\s*</div>\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EpisodeCardRegex();



    [GeneratedRegex(@"data-t=""description""[^>]*>([^<]+)<", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeDescriptionRegex();

    [GeneratedRegex(@"<img[^>]*class=""[^""]*content-image__image[^""]*""[^>]*src=""(https://imgsrv\.crunchyroll\.com[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeThumbnailRegex();

    [GeneratedRegex(@"data-t=""duration-info""[^>]*>(\d+m)<", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeDurationRegex();

    [GeneratedRegex(@"<div[^>]*class=""[^""]*browse-card[^""]*""[^>]*>.*?</div>\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SeriesCardRegex();

    [GeneratedRegex(@"href=""[^""]*?/series/([A-Z0-9]+)/([a-z0-9-]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesLinkRegex();

    [GeneratedRegex(@"<h4[^>]*>([^<]+)</h4>", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesTitleInCardRegex();

    [GeneratedRegex(@"<img[^>]*src=""(https://imgsrv\.crunchyroll\.com[^""]+)""[^>]*alt=""", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesImageInCardRegex();
}
