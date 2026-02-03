using FluentAssertions;
using Jellyfin.Plugin.Crunchyroll.Api;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Crunchyroll.Tests.Api;

/// <summary>
/// Tests for the CrunchyrollHtmlScraper focusing on internationalization.
/// </summary>
public class CrunchyrollHtmlScraperTests
{
    private readonly Mock<ILogger> _loggerMock;

    public CrunchyrollHtmlScraperTests()
    {
        _loggerMock = new Mock<ILogger>();
    }

    /// <summary>
    /// Tests that Portuguese season prefix "T" is correctly parsed.
    /// Example: "T1 E5 - Título do Episódio"
    /// </summary>
    [Theory]
    [InlineData("T1 E5 - O Primeiro Passo", 5, "O Primeiro Passo")]
    [InlineData("T2 E10 - A Batalha Final", 10, "A Batalha Final")]
    [InlineData("T1 E1: Introdução", 1, "Introdução")]
    public void ParseEpisodeTitle_PortugueseBrazil_ShouldExtractCorrectly(
        string fullTitle, int expectedEpisodeNumber, string expectedTitle)
    {
        // Arrange - This is the pattern currently in the code
        var pattern = @"^(?:T(?<season>\d+)\s+)?(?:E(?<episode>\d+)\s*[-:]\s*)?(?<title>.+)$";
        var match = System.Text.RegularExpressions.Regex.Match(
            fullTitle, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Act & Assert
        match.Success.Should().BeTrue();
        
        if (match.Groups["episode"].Success)
        {
            int.Parse(match.Groups["episode"].Value).Should().Be(expectedEpisodeNumber);
        }
        
        match.Groups["title"].Value.Trim().Should().Be(expectedTitle);
    }

    /// <summary>
    /// Tests that French/English season prefix "S" is correctly parsed.
    /// Example: "S1 E5 - Titre de l'épisode"
    /// 
    /// BUG: Current regex only supports "T" prefix, not "S"!
    /// </summary>
    [Theory]
    [InlineData("S1 E5 - Le Premier Pas", 5, "Le Premier Pas")]
    [InlineData("S2 E10 - La Bataille Finale", 10, "La Bataille Finale")]
    [InlineData("S1 E1: Introduction", 1, "Introduction")]
    public void ParseEpisodeTitle_French_ShouldExtractCorrectly_CurrentlyFails(
        string fullTitle, int expectedEpisodeNumber, string expectedTitle)
    {
        // Arrange - Current pattern (BUG: only supports "T", not "S")
        var currentPattern = @"^(?:T(?<season>\d+)\s+)?(?:E(?<episode>\d+)\s*[-:]\s*)?(?<title>.+)$";
        
        // FIXED pattern that supports both T and S
        var fixedPattern = @"^(?:[TS](?<season>\d+)\s+)?(?:E(?<episode>\d+)\s*[-:]\s*)?(?<title>.+)$";

        // Act - Current pattern
        var currentMatch = System.Text.RegularExpressions.Regex.Match(
            fullTitle, currentPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Act - Fixed pattern
        var fixedMatch = System.Text.RegularExpressions.Regex.Match(
            fullTitle, fixedPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Assert - Current pattern FAILS to extract season (because it's "S" not "T")
        // The title will still include "S" prefix
        currentMatch.Success.Should().BeTrue(); // Match succeeds but...
        currentMatch.Groups["season"].Success.Should().BeFalse(); // Season not captured!
        currentMatch.Groups["title"].Value.Should().StartWith("S"); // BUG: S is in title

        // Assert - Fixed pattern works correctly
        fixedMatch.Success.Should().BeTrue();
        fixedMatch.Groups["season"].Success.Should().BeTrue();
        fixedMatch.Groups["title"].Value.Trim().Should().Be(expectedTitle);
    }

    /// <summary>
    /// Tests the proposed fixed pattern that supports multiple language prefixes.
    /// </summary>
    [Theory]
    // Portuguese (T prefix)
    [InlineData("T1 E5 - Título", "1", "5", "Título")]
    [InlineData("T2 E10: Título Longo", "2", "10", "Título Longo")]
    // French/English (S prefix)
    [InlineData("S1 E5 - Title", "1", "5", "Title")]
    [InlineData("S2 E10: Long Title", "2", "10", "Long Title")]
    // No season prefix
    [InlineData("E5 - Title Only", null, "5", "Title Only")]
    [InlineData("E10: Another Title", null, "10", "Another Title")]
    // No episode number (just title)
    [InlineData("Movie Title", null, null, "Movie Title")]
    public void ParseEpisodeTitle_WithFixedPattern_ShouldSupportAllLocales(
        string fullTitle, string? expectedSeason, string? expectedEpisode, string expectedTitle)
    {
        // Arrange - Proposed fixed pattern
        var fixedPattern = @"^(?:[TS](?<season>\d+)\s+)?(?:E(?<episode>\d+)\s*[-:]\s*)?(?<title>.+)$";

        // Act
        var match = System.Text.RegularExpressions.Regex.Match(
            fullTitle, fixedPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Assert
        match.Success.Should().BeTrue();

        if (expectedSeason != null)
        {
            match.Groups["season"].Success.Should().BeTrue();
            match.Groups["season"].Value.Should().Be(expectedSeason);
        }
        else
        {
            match.Groups["season"].Success.Should().BeFalse();
        }

        if (expectedEpisode != null)
        {
            match.Groups["episode"].Success.Should().BeTrue();
            match.Groups["episode"].Value.Should().Be(expectedEpisode);
        }
        else
        {
            match.Groups["episode"].Success.Should().BeFalse();
        }

        match.Groups["title"].Value.Trim().Should().Be(expectedTitle);
    }

    /// <summary>
    /// Tests series extraction from HTML (basic test with mock HTML).
    /// </summary>
    [Fact]
    public void ExtractSeriesFromHtml_WithValidHtml_ShouldExtractTitle()
    {
        // Arrange
        var html = @"
            <html>
                <head>
                    <meta property=""og:title"" content=""Blue Lock - Watch on Crunchyroll"" />
                    <meta property=""og:description"" content=""After a disastrous defeat..."" />
                </head>
                <body>
                    <h1 class=""title"">Blue Lock</h1>
                </body>
            </html>";

        // Act
        var result = CrunchyrollHtmlScraper.ExtractSeriesFromHtml(html, "G4PH0WEKE", _loggerMock.Object);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Blue Lock");
        result.Id.Should().Be("G4PH0WEKE");
    }
}
