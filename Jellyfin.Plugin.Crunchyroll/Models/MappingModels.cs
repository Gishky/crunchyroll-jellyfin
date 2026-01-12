namespace Jellyfin.Plugin.Crunchyroll.Models;

/// <summary>
/// Represents the mapping between Jellyfin episode numbers and Crunchyroll episode numbers.
/// This is used to handle cases where episode numbering differs between the local library and Crunchyroll.
/// </summary>
public class EpisodeMapping
{
    /// <summary>
    /// Gets or sets the Crunchyroll series ID.
    /// </summary>
    public string? CrunchyrollSeriesId { get; set; }

    /// <summary>
    /// Gets or sets the Crunchyroll season ID.
    /// </summary>
    public string? CrunchyrollSeasonId { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin season number (as organized in the user's library).
    /// </summary>
    public int JellyfinSeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode offset.
    /// If Crunchyroll starts Season 2 at episode 25, but Jellyfin has it as episode 1,
    /// the offset would be 24 (CrunchyrollEpisodeNumber = JellyfinEpisodeNumber + Offset).
    /// </summary>
    public int EpisodeOffset { get; set; }

    /// <summary>
    /// Gets or sets the first episode number in this season according to Crunchyroll.
    /// </summary>
    public int CrunchyrollFirstEpisode { get; set; }

    /// <summary>
    /// Gets or sets the last episode number in this season according to Crunchyroll.
    /// </summary>
    public int CrunchyrollLastEpisode { get; set; }

    /// <summary>
    /// Gets or sets the total number of episodes in this season.
    /// </summary>
    public int TotalEpisodes { get; set; }
}

/// <summary>
/// Represents the complete season mapping for a series.
/// </summary>
public class SeasonMapping
{
    /// <summary>
    /// Gets or sets the Crunchyroll series ID.
    /// </summary>
    public string? CrunchyrollSeriesId { get; set; }

    /// <summary>
    /// Gets or sets the series title.
    /// </summary>
    public string? SeriesTitle { get; set; }

    /// <summary>
    /// Gets or sets the list of season mappings.
    /// </summary>
    public List<SeasonMappingEntry> Seasons { get; set; } = new();
}

/// <summary>
/// Individual season mapping entry.
/// </summary>
public class SeasonMappingEntry
{
    /// <summary>
    /// Gets or sets the Jellyfin season number (user's organization).
    /// </summary>
    public int JellyfinSeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the Crunchyroll season ID.
    /// </summary>
    public string? CrunchyrollSeasonId { get; set; }

    /// <summary>
    /// Gets or sets the Crunchyroll season number.
    /// </summary>
    public int CrunchyrollSeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the season title from Crunchyroll.
    /// </summary>
    public string? CrunchyrollSeasonTitle { get; set; }

    /// <summary>
    /// Gets or sets the episode offset to convert Jellyfin episode numbers to Crunchyroll episode numbers.
    /// </summary>
    public int EpisodeOffset { get; set; }
}

/// <summary>
/// Result of episode matching operation.
/// </summary>
public class EpisodeMatchResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the match was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the matched Crunchyroll episode.
    /// </summary>
    public CrunchyrollEpisode? Episode { get; set; }

    /// <summary>
    /// Gets or sets the confidence level of the match (0-100).
    /// </summary>
    public int Confidence { get; set; }

    /// <summary>
    /// Gets or sets any notes about the matching process.
    /// </summary>
    public string? Notes { get; set; }
}
