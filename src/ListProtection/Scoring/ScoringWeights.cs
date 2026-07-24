using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Authoritative signal weight registry.
    ///
    /// All weights live here — adding a new signal means:
    ///   1. Add a constant in the appropriate group below.
    ///   2. Register it in the lookup dictionary below.
    ///   3. Fire the EvidenceFact in the correct IEvidenceCollector.
    ///   Done — the scorer picks it up automatically.
    ///
    /// ── Design philosophy ─────────────────────────────────────────────────
    ///
    /// Signals are COMBINATIONS, not individual fields. A track number alone
    /// is meaningless; track number + album is a strong identity signal.
    /// Scores across all media types are calibrated to the same 0–200 scale
    /// so that shared thresholds (AutoRepairScoreThreshold, MinCandidateDistance)
    /// apply consistently regardless of media type.
    ///
    /// ── BaseItem signals (all media types) ────────────────────────────────
    ///
    ///   FilenameStemExact       25  Stem without extension, case-insensitive exact
    ///   FilenameStemNormalized  15  After stripping track-number prefix ("02. ")
    ///   NameExact               40  item.Name exact, case-insensitive
    ///   NameNormalized          20  Whitespace-collapsed lowercase
    ///   ParentFolderMatch       15  Immediate parent folder name
    ///   GrandparentFolderMatch  10  Two levels up
    ///
    /// ── Audio signals ─────────────────────────────────────────────────────
    ///
    ///   MbTrackIdMatch         200  MusicBrainz Track ID exact match — definitive
    ///   NameArtistAlbumIndex   170  Name + Artist + Album + TrackNumber all match
    ///   NameArtistAlbum        150  Name + Artist + Album all match
    ///   NameArtistDuration     140  Name + Artist + Duration within tolerance
    ///   NameAlbumIndex         120  Name + Album + TrackNumber match
    ///   DurationAlbumIndex     110  Duration + Album + TrackNumber match
    ///   NameArtist              80  Name + Artist match (no album)
    ///   NameDuration            70  Name + Duration within tolerance
    ///   AlbumIndex              50  Album + TrackNumber match
    ///   NameOnly                30  Name alone (risk of same-name tracks)
    ///   ArtistAlbum             25  Artist + Album without name (low identity value)
    ///   FolderMatch             20  Parent folder name
    ///   FilenameSimilar         15  Filename stem after normalisation
    ///
    /// ── Episode signals ───────────────────────────────────────────────────
    ///
    ///   TvdbIdMatch            200  TVDB episode ID match — definitive
    ///   ImdbIdMatch            200  IMDB episode ID match — definitive
    ///   SeriesSeasonEpDuration 160  Series + Season + EpisodeNumber + Duration
    ///   SeriesSeasonEpTitle    170  Series + Season + EpisodeNumber + Title
    ///   SeriesSeasonEp         150  Series + Season + EpisodeNumber
    ///   SeriesEpDuration       120  Series + EpisodeNumber + Duration (no season)
    ///   SeriesTitleDuration    110  Series + Title + Duration
    ///   SeriesSeasonTitle      100  Series + Season + Title
    ///   SeriesTitle             70  Series + Title
    ///   SeriesDuration          50  Series + Duration only
    ///   SeriesOnly              20  Series name alone
    ///
    /// ── Movie signals ─────────────────────────────────────────────────────
    ///
    ///   ImdbMovieIdMatch       200  IMDB movie ID match — definitive
    ///   TmdbMovieIdMatch       200  TMDB movie ID match — definitive
    ///   TitleYearDuration      175  Title + Year + Duration within tolerance
    ///   TitleYear              150  Title (exact) + Year
    ///   TitleDuration          120  Title (exact) + Duration within tolerance
    ///   TitleOnly               70  Title exact, no year/duration corroboration
    ///   DurationOnly            40  Duration match alone (very weak for movies)
    /// </summary>
    public static class ScoringWeights
    {
        // ── BaseItem signals ───────────────────────────────────────────────

        public const int FilenameStemExact = 25;
        public const int FilenameStemNormalized = 15;
        public const int NameExact = 40;
        public const int NameNormalized = 20;
        public const int ParentFolderMatch = 15;
        public const int GrandparentFolderMatch = 10;

        // ── Audio combination signals ──────────────────────────────────────

        public const int MbTrackIdMatch = 200;
        public const int NameArtistAlbumIndex = 170;
        public const int NameArtistAlbum = 150;
        public const int NameArtistDuration = 140;
        public const int NameAlbumIndex = 120;
        public const int DurationAlbumIndex = 110;
        public const int NameArtist = 80;
        public const int NameDuration = 70;
        public const int AlbumIndex = 50;
        public const int NameOnly = 30;
        public const int ArtistAlbum = 25;
        public const int AudioFolderMatch = 20;
        public const int AudioFilenameSimilar = 15;

        // ── Episode combination signals ────────────────────────────────────

        public const int TvdbIdMatch = 200;
        public const int ImdbEpIdMatch = 200;
        public const int SeriesSeasonEpTitle = 170;
        public const int SeriesSeasonEpDuration = 160;
        public const int SeriesSeasonEp = 150;
        public const int SeriesEpDuration = 120;
        public const int SeriesTitleDuration = 110;
        public const int SeriesSeasonTitle = 100;
        public const int SeriesTitle = 70;
        public const int SeriesDuration = 50;
        public const int SeriesOnly = 20;

        // ── Movie combination signals ──────────────────────────────────────

        public const int ImdbMovieIdMatch = 200;
        public const int TmdbMovieIdMatch = 200;
        public const int TitleYearDuration = 175;
        public const int TitleYear = 150;
        public const int TitleDuration = 120;
        public const int TitleOnly = 70;
        public const int DurationOnly = 40;

        // ── Lookup table (signal name → weight) ───────────────────────────

        private static readonly Dictionary<string, int> _weights =
            new Dictionary<string, int>
            {
                // BaseItem
                { nameof(FilenameStemExact),      FilenameStemExact      },
                { nameof(FilenameStemNormalized), FilenameStemNormalized },
                { nameof(NameExact),              NameExact              },
                { nameof(NameNormalized),         NameNormalized         },
                { nameof(ParentFolderMatch),      ParentFolderMatch      },
                { nameof(GrandparentFolderMatch), GrandparentFolderMatch },

                // Audio
                { nameof(MbTrackIdMatch),         MbTrackIdMatch         },
                { nameof(NameArtistAlbumIndex),   NameArtistAlbumIndex   },
                { nameof(NameArtistAlbum),        NameArtistAlbum        },
                { nameof(NameArtistDuration),     NameArtistDuration     },
                { nameof(NameAlbumIndex),         NameAlbumIndex         },
                { nameof(DurationAlbumIndex),     DurationAlbumIndex     },
                { nameof(NameArtist),             NameArtist             },
                { nameof(NameDuration),           NameDuration           },
                { nameof(AlbumIndex),             AlbumIndex             },
                { nameof(NameOnly),               NameOnly               },
                { nameof(ArtistAlbum),            ArtistAlbum            },
                { nameof(AudioFolderMatch),       AudioFolderMatch       },
                { nameof(AudioFilenameSimilar),   AudioFilenameSimilar   },

                // Episode
                { nameof(TvdbIdMatch),            TvdbIdMatch            },
                { nameof(ImdbEpIdMatch),          ImdbEpIdMatch          },
                { nameof(SeriesSeasonEpTitle),    SeriesSeasonEpTitle    },
                { nameof(SeriesSeasonEpDuration), SeriesSeasonEpDuration },
                { nameof(SeriesSeasonEp),         SeriesSeasonEp         },
                { nameof(SeriesEpDuration),       SeriesEpDuration       },
                { nameof(SeriesTitleDuration),    SeriesTitleDuration    },
                { nameof(SeriesSeasonTitle),      SeriesSeasonTitle      },
                { nameof(SeriesTitle),            SeriesTitle            },
                { nameof(SeriesDuration),         SeriesDuration         },
                { nameof(SeriesOnly),             SeriesOnly             },

                // Movie
                { nameof(ImdbMovieIdMatch),       ImdbMovieIdMatch       },
                { nameof(TmdbMovieIdMatch),       TmdbMovieIdMatch       },
                { nameof(TitleYearDuration),      TitleYearDuration      },
                { nameof(TitleYear),              TitleYear              },
                { nameof(TitleDuration),          TitleDuration          },
                { nameof(TitleOnly),              TitleOnly              },
                { nameof(DurationOnly),           DurationOnly           },
            };

        /// <summary>Returns the weight for a signal name, or 0 if unrecognised.</summary>
        public static int Get(string signalName)
        {
            if (string.IsNullOrEmpty(signalName)) return 0;
            return _weights.TryGetValue(signalName, out var w) ? w : 0;
        }

        /// <summary>
        /// Returns all registered signals grouped by media type for display purposes.
        /// Key = group label, Value = list of (SignalName, Weight, Description).
        /// </summary>
        public static Dictionary<string, List<(string Signal, int Weight, string Description)>> GetScoringReference()
        {
            return new Dictionary<string, List<(string, int, string)>>
            {
                ["Audio"] = new List<(string, int, string)>
                {
                    (nameof(MbTrackIdMatch),       MbTrackIdMatch,       "MusicBrainz Track ID exact match"),
                    (nameof(NameArtistAlbumIndex), NameArtistAlbumIndex, "Name + Artist + Album + Track number"),
                    (nameof(NameArtistAlbum),      NameArtistAlbum,      "Name + Artist + Album"),
                    (nameof(NameArtistDuration),   NameArtistDuration,   "Name + Artist + Duration (within tolerance)"),
                    (nameof(NameAlbumIndex),       NameAlbumIndex,       "Name + Album + Track number"),
                    (nameof(DurationAlbumIndex),   DurationAlbumIndex,   "Duration + Album + Track number"),
                    (nameof(NameArtist),           NameArtist,           "Name + Artist (no album)"),
                    (nameof(NameDuration),         NameDuration,         "Name + Duration (within tolerance)"),
                    (nameof(AlbumIndex),           AlbumIndex,           "Album + Track number"),
                    (nameof(NameOnly),             NameOnly,             "Name alone"),
                    (nameof(ArtistAlbum),          ArtistAlbum,          "Artist + Album (no name)"),
                    (nameof(AudioFolderMatch),     AudioFolderMatch,     "Parent folder name match"),
                    (nameof(AudioFilenameSimilar), AudioFilenameSimilar, "Filename stem after normalisation"),
                },
                ["Episode"] = new List<(string, int, string)>
                {
                    (nameof(TvdbIdMatch),            TvdbIdMatch,            "TVDB episode ID match"),
                    (nameof(ImdbEpIdMatch),          ImdbEpIdMatch,          "IMDB episode ID match"),
                    (nameof(SeriesSeasonEpTitle),    SeriesSeasonEpTitle,    "Series + Season + Episode number + Title"),
                    (nameof(SeriesSeasonEpDuration), SeriesSeasonEpDuration, "Series + Season + Episode number + Duration"),
                    (nameof(SeriesSeasonEp),         SeriesSeasonEp,         "Series + Season + Episode number"),
                    (nameof(SeriesEpDuration),       SeriesEpDuration,       "Series + Episode number + Duration"),
                    (nameof(SeriesTitleDuration),    SeriesTitleDuration,    "Series + Title + Duration"),
                    (nameof(SeriesSeasonTitle),      SeriesSeasonTitle,      "Series + Season + Title"),
                    (nameof(SeriesTitle),            SeriesTitle,            "Series + Title"),
                    (nameof(SeriesDuration),         SeriesDuration,         "Series + Duration"),
                    (nameof(SeriesOnly),             SeriesOnly,             "Series name alone"),
                },
                ["Movie"] = new List<(string, int, string)>
                {
                    (nameof(ImdbMovieIdMatch),   ImdbMovieIdMatch,   "IMDB movie ID match"),
                    (nameof(TmdbMovieIdMatch),   TmdbMovieIdMatch,   "TMDB movie ID match"),
                    (nameof(TitleYearDuration),  TitleYearDuration,  "Title + Year + Duration (within tolerance)"),
                    (nameof(TitleYear),          TitleYear,          "Title + Year"),
                    (nameof(TitleDuration),      TitleDuration,      "Title + Duration (within tolerance)"),
                    (nameof(TitleOnly),          TitleOnly,          "Title exact (no year or duration)"),
                    (nameof(DurationOnly),       DurationOnly,       "Duration match alone"),
                },
                ["All media types"] = new List<(string, int, string)>
                {
                    (nameof(NameExact),              NameExact,              "Item name exact match"),
                    (nameof(NameNormalized),         NameNormalized,         "Item name after whitespace normalisation"),
                    (nameof(FilenameStemExact),      FilenameStemExact,      "Filename stem exact match"),
                    (nameof(FilenameStemNormalized), FilenameStemNormalized, "Filename stem after track-prefix stripping"),
                    (nameof(ParentFolderMatch),      ParentFolderMatch,      "Parent folder name match"),
                    (nameof(GrandparentFolderMatch), GrandparentFolderMatch, "Grandparent folder name match"),
                },
            };
        }
    }
}