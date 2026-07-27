using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Prioritised rule table for ContentScore.
    /// Rules are evaluated top-to-bottom; first matching rule sets ContentScore.
    /// Order is critical — most specific combination must precede less specific.
    ///
    /// LocationScore is computed separately by summing FolderFacts.Weights[depth].
    /// FallbackScore is computed separately when ContentScore == 0.
    ///
    /// To add a signal: add atomic fact in the relevant collector, add rule(s) here.
    /// To change a weight: change the value here only.
    /// </summary>
    public static class ScoringWeights
    {
        // ── Audio content rules ────────────────────────────────────────────

        public static readonly List<ScoringRule> AudioRules = new List<ScoringRule>
        {
            // Definitive
            new ScoringRule(200, AudioFacts.MbTrackIdMatch),

            // Name + Artist + Album + Track — four-field anchor
            new ScoringRule(170, AudioFacts.NameMatch, AudioFacts.ArtistMatch, AudioFacts.AlbumMatch, AudioFacts.TrackNumberMatch),

            // Name + Artist + Album
            new ScoringRule(150, AudioFacts.NameMatch, AudioFacts.ArtistMatch, AudioFacts.AlbumMatch),

            // Name + Artist + Duration
            new ScoringRule(140, AudioFacts.NameMatch, AudioFacts.ArtistMatch, AudioFacts.DurationMatch),

            // Name + Album + Track
            new ScoringRule(120, AudioFacts.NameMatch, AudioFacts.AlbumMatch, AudioFacts.TrackNumberMatch),

            // Duration + Album + Track
            new ScoringRule(110, AudioFacts.DurationMatch, AudioFacts.AlbumMatch, AudioFacts.TrackNumberMatch),

            // Name + Artist
            new ScoringRule(80, AudioFacts.NameMatch, AudioFacts.ArtistMatch),

            // Name + Duration
            new ScoringRule(70, AudioFacts.NameMatch, AudioFacts.DurationMatch),

            // Album + Track
            new ScoringRule(50, AudioFacts.AlbumMatch, AudioFacts.TrackNumberMatch),

            // Name alone
            new ScoringRule(30, AudioFacts.NameMatch),

            // Artist + Album (no name — weakest)
            new ScoringRule(25, AudioFacts.ArtistMatch, AudioFacts.AlbumMatch),
        };

        // ── Episode content rules ──────────────────────────────────────────

        public static readonly List<ScoringRule> EpisodeRules = new List<ScoringRule>
        {
            new ScoringRule(170, EpisodeFacts.SeriesMatch, EpisodeFacts.SeasonMatch, EpisodeFacts.EpisodeMatch, EpisodeFacts.TitleMatch),
            new ScoringRule(160, EpisodeFacts.SeriesMatch, EpisodeFacts.SeasonMatch, EpisodeFacts.EpisodeMatch, EpisodeFacts.DurationMatch),
            new ScoringRule(150, EpisodeFacts.SeriesMatch, EpisodeFacts.SeasonMatch, EpisodeFacts.EpisodeMatch),
            new ScoringRule(120, EpisodeFacts.SeriesMatch, EpisodeFacts.EpisodeMatch, EpisodeFacts.DurationMatch),
            new ScoringRule(110, EpisodeFacts.SeriesMatch, EpisodeFacts.TitleMatch, EpisodeFacts.DurationMatch),
            new ScoringRule(100, EpisodeFacts.SeriesMatch, EpisodeFacts.SeasonMatch, EpisodeFacts.TitleMatch),
            new ScoringRule(70,  EpisodeFacts.SeriesMatch, EpisodeFacts.TitleMatch),
            new ScoringRule(50,  EpisodeFacts.SeriesMatch, EpisodeFacts.DurationMatch),
            new ScoringRule(20,  EpisodeFacts.SeriesMatch),
        };

        // ── Movie content rules ────────────────────────────────────────────

        public static readonly List<ScoringRule> MovieRules = new List<ScoringRule>
        {
            new ScoringRule(200, MovieFacts.ImdbIdMatch),
            new ScoringRule(200, MovieFacts.TmdbIdMatch),
            new ScoringRule(175, MovieFacts.TitleMatch, MovieFacts.YearMatch, MovieFacts.DurationMatch),
            new ScoringRule(150, MovieFacts.TitleMatch, MovieFacts.YearMatch),
            new ScoringRule(120, MovieFacts.TitleMatch, MovieFacts.DurationMatch),
            new ScoringRule(70,  MovieFacts.TitleMatch),
            new ScoringRule(40,  MovieFacts.DurationMatch),
        };

        // ── Series content rules (whole series as a direct collection member) ─

        public static readonly List<ScoringRule> SeriesRules = new List<ScoringRule>
        {
            new ScoringRule(200, SeriesFacts.TvdbIdMatch),
            new ScoringRule(200, SeriesFacts.ImdbIdMatch),
            new ScoringRule(70,  SeriesFacts.TitleMatch),
        };

        // ── MusicAlbum content rules (whole album as a direct collection member) ─
        // Conservative — see MusicAlbumEvidenceCollector for why no ID short-circuit exists yet.

        public static readonly List<ScoringRule> MusicAlbumRules = new List<ScoringRule>
        {
            new ScoringRule(90, MusicAlbumFacts.TitleMatch, MusicAlbumFacts.YearMatch),
            new ScoringRule(50, MusicAlbumFacts.TitleMatch),
        };

        // ── Fallback content rules (Tier 3 — no media-type collector fired) ─

        public static readonly List<ScoringRule> FallbackRules = new List<ScoringRule>
        {
            new ScoringRule(60, FallbackFacts.NameExact,        FallbackFacts.FilenameStemExact),
            new ScoringRule(50, FallbackFacts.NameExact,        FallbackFacts.FilenameStemNorm),
            new ScoringRule(45, FallbackFacts.NameNormalized,   FallbackFacts.FilenameStemExact),
            new ScoringRule(40, FallbackFacts.NameExact),
            new ScoringRule(25, FallbackFacts.FilenameStemExact),
            new ScoringRule(20, FallbackFacts.NameNormalized),
            new ScoringRule(15, FallbackFacts.FilenameStemNorm),
        };

        /// <summary>
        /// Returns the rules list for a given media type.
        /// Null mediaType returns FallbackRules.
        /// </summary>
        public static List<ScoringRule> RulesFor(string mediaType)
        {
            if (string.IsNullOrEmpty(mediaType)) return FallbackRules;
            switch (mediaType)
            {
                case "Audio": return AudioRules;
                case "Episode": return EpisodeRules;
                case "Movie": return MovieRules;
                case "Series": return SeriesRules;
                case "MusicAlbum": return MusicAlbumRules;
                default: return FallbackRules;
            }
        }
    }

    /// <summary>
    /// A single scoring rule: if all RequiredFacts are present, apply Score.
    /// Rules are evaluated in list order — first match wins.
    /// </summary>
    public sealed class ScoringRule
    {
        public int Score { get; }
        public IReadOnlyList<string> RequiredFacts { get; }

        public ScoringRule(int score, params string[] requiredFacts)
        {
            Score = score;
            RequiredFacts = requiredFacts;
        }

        public bool Matches(HashSet<string> presentFacts)
        {
            foreach (var f in RequiredFacts)
                if (!presentFacts.Contains(f)) return false;
            return true;
        }
    }
}