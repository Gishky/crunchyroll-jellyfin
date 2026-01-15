using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

/// <summary>
/// Simple console application to test the Crunchyroll HTML scraper locally.
/// Run this to fetch HTML from Crunchyroll (via FlareSolverr) and test the parsing logic.
/// </summary>
class ScraperTest
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Crunchyroll HTML Scraper Test Tool ===\n");
        
        string seriesId = args.Length > 0 ? args[0] : "GRDV0019R";  // Default: Jujutsu Kaisen
        string flareSolverrUrl = args.Length > 1 ? args[1] : "http://localhost:8191";
        
        Console.WriteLine($"Series ID: {seriesId}");
        Console.WriteLine($"FlareSolverr URL: {flareSolverrUrl}");
        Console.WriteLine();
        
        // Option 1: Test with existing HTML file
        if (args.Length > 0 && File.Exists(args[0]))
        {
            Console.WriteLine($"Testing with local file: {args[0]}");
            var html = await File.ReadAllTextAsync(args[0]);
            AnalyzeHtml(html);
            return;
        }
        
        // Option 2: Fetch from FlareSolverr
        Console.WriteLine("Fetching page via FlareSolverr...");
        var pageHtml = await FetchViaFlareSolverr(flareSolverrUrl, $"https://www.crunchyroll.com/series/{seriesId}");
        
        if (string.IsNullOrEmpty(pageHtml))
        {
            Console.WriteLine("ERROR: Failed to fetch page");
            return;
        }
        
        // Save HTML for later analysis
        var outputFile = $"crunchyroll_test_{seriesId}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        await File.WriteAllTextAsync(outputFile, pageHtml);
        Console.WriteLine($"\nSaved HTML to: {outputFile}");
        
        AnalyzeHtml(pageHtml);
    }
    
    static void AnalyzeHtml(string html)
    {
        Console.WriteLine("\n=== HTML Analysis ===");
        Console.WriteLine($"Total length: {html.Length} chars");
        Console.WriteLine();
        
        // Check for key indicators
        var indicators = new[]
        {
            ("episode-card", html.Contains("episode-card")),
            ("playable-card", html.Contains("playable-card")),
            ("erc-playable-card", html.Contains("erc-playable-card")),
            ("data-t=", html.Contains("data-t=")),
            ("/watch/", html.Contains("/watch/")),
            ("__INITIAL_STATE__", html.Contains("__INITIAL_STATE__")),
            ("__NEXT_DATA__", html.Contains("__NEXT_DATA__")),
            ("Just a moment", html.Contains("Just a moment")),  // Cloudflare challenge
            ("cf-", html.Contains("cf-")),  // Cloudflare elements
        };
        
        Console.WriteLine("Key indicators:");
        foreach (var (name, found) in indicators)
        {
            Console.WriteLine($"  {name}: {(found ? "FOUND" : "not found")}");
        }
        
        // Count occurrences
        Console.WriteLine("\nOccurrence counts:");
        Console.WriteLine($"  'episode': {CountOccurrences(html, "episode")}");
        Console.WriteLine($"  '/watch/': {CountOccurrences(html, "/watch/")}");
        Console.WriteLine($"  'data-t=': {CountOccurrences(html, "data-t=")}");
        
        // Try to extract watch links
        Console.WriteLine("\n=== Watch Links Found ===");
        var watchPattern = new System.Text.RegularExpressions.Regex(
            @"href=""[^""]*?/watch/([A-Z0-9]{9,})(?:/([^""]+))?""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        var matches = watchPattern.Matches(html);
        var uniqueIds = new System.Collections.Generic.HashSet<string>();
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var id = match.Groups[1].Value;
            if (!uniqueIds.Contains(id))
            {
                uniqueIds.Add(id);
                var slug = match.Groups[2].Success ? match.Groups[2].Value : "(no slug)";
                Console.WriteLine($"  {id}: {slug}");
            }
        }
        
        Console.WriteLine($"\nTotal unique episode IDs: {uniqueIds.Count}");
        
        // Show first 3000 chars for manual inspection
        Console.WriteLine("\n=== First 3000 chars of HTML ===");
        Console.WriteLine(html.Substring(0, Math.Min(3000, html.Length)));
        
        // Look for episode-related elements
        Console.WriteLine("\n=== Looking for Episode Elements ===");
        
        var episodePatterns = new[]
        {
            @"data-t=""episode[^""]*""",
            @"class=""[^""]*episode[^""]*""",
            @"class=""[^""]*playable-card[^""]*""",
            @"class=""[^""]*erc-[^""]*""",
        };
        
        foreach (var pattern in episodePatterns)
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var found = regex.Matches(html);
            Console.WriteLine($"  Pattern '{pattern}': {found.Count} matches");
            if (found.Count > 0 && found.Count <= 5)
            {
                foreach (System.Text.RegularExpressions.Match m in found)
                {
                    Console.WriteLine($"    -> {m.Value}");
                }
            }
        }
    }
    
    static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
    
    static async Task<string?> FetchViaFlareSolverr(string flareSolverrUrl, string targetUrl)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            
            var request = new
            {
                cmd = "request.get",
                url = targetUrl,
                maxTimeout = 60000
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            Console.WriteLine($"Requesting: {targetUrl}");
            var response = await client.PostAsync($"{flareSolverrUrl}/v1", content);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"FlareSolverr error: {response.StatusCode}");
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine(errorBody);
                return null;
            }
            
            var responseJson = await response.Content.ReadAsStringAsync();
            
            // Parse response to get HTML
            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("solution", out var solution) && 
                solution.TryGetProperty("response", out var htmlElement))
            {
                var htmlContent = htmlElement.GetString();
                Console.WriteLine($"Got {htmlContent?.Length ?? 0} chars of HTML");
                return htmlContent;
            }
            
            Console.WriteLine("Could not find 'solution.response' in FlareSolverr response");
            Console.WriteLine(responseJson.Substring(0, Math.Min(500, responseJson.Length)));
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return null;
        }
    }
}
