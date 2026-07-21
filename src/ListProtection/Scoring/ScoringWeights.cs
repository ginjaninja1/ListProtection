using System.Collections.Generic;

namespace ListProtection.Scoring
{
    /// <summary>
    /// Authoritative signal weight registry.
    ///
    /// All weights live here — adding a new signal means:
    ///   1. Add a constant in the appropriate group below.
    ///   2. Fire the EvidenceFact in the correct IEvidenceCollector.
    ///   Done — the scorer picks it up automatically.
    ///
    /// Weight philosophy:
    ///   100+ = sufficient alone to be confident — suitable for auto-repair
    ///    50–99 = strong supporting signal — needs corroboration
    ///    20–49 = useful tiebreaker — too weak alone
    ///    1–19  = weak contextual hint — rarely decisive
    ///
    /// AutoRepair default threshold is 100 (FilenameStemExact alone).
    /// A combined audio score of FilenameStemExact(100) + NameExact(60) = 160
    /// means very high confidence.
    ///
    /// ── BaseItem signals (all media types) ────────────────────────────────
    ///
    ///   FilenameStemExact      100  Stem without extension, case-insensitive exact
    ///   FilenameStemNormalized  70  After stripping track-number prefix ("02. ")
    ///   NameExact               60  item.Name exact, case-insensitive
    ///   NameNormalized          40  whitespace-collapsed lowercase
    ///   ParentFolderMatch       20  Immediate parent folder name
    ///   GrandparentFolderMatch  15  Two levels up (artist folder for music)
    ///
    /// ── Audio-specific signals ─────────────────────────────────────────────
    ///
    ///   AlbumExact              50  item.Album exact, case-insensitive
    ///   AlbumArtistExact        30  item.AlbumArtists[0] exact, case-insensitive
    ///   IndexNumberMatch        20  item.IndexNumber == gt.IndexNumber
    ///   DurationMatch           40  |RunTimeTicks delta| within 3 seconds
    /// </summary>
    public static class ScoringWeights
    {
        // ── BaseItem signals ───────────────────────────────────────────────

        public const int FilenameStemExact = 100;
        public const int FilenameStemNormalized = 70;
        public const int NameExact = 60;
        public const int NameNormalized = 40;
        public const int ParentFolderMatch = 20;
        public const int GrandparentFolderMatch = 15;

        // ── Audio-specific signals ─────────────────────────────────────────

        public const int AlbumExact = 50;
        public const int AlbumArtistExact = 30;
        public const int IndexNumberMatch = 20;
        public const int DurationMatch = 40;

        // ── Lookup table (signal name → weight) ───────────────────────────

        private static readonly Dictionary<string, int> _weights =
            new Dictionary<string, int>
            {
                { nameof(FilenameStemExact),      FilenameStemExact      },
                { nameof(FilenameStemNormalized), FilenameStemNormalized },
                { nameof(NameExact),              NameExact              },
                { nameof(NameNormalized),         NameNormalized         },
                { nameof(ParentFolderMatch),      ParentFolderMatch      },
                { nameof(GrandparentFolderMatch), GrandparentFolderMatch },
                { nameof(AlbumExact),             AlbumExact             },
                { nameof(AlbumArtistExact),       AlbumArtistExact       },
                { nameof(IndexNumberMatch),       IndexNumberMatch       },
                { nameof(DurationMatch),          DurationMatch          },
            };

        /// <summary>
        /// Returns the weight for a signal name, or 0 if unrecognised.
        /// </summary>
        public static int Get(string signalName)
        {
            if (string.IsNullOrEmpty(signalName)) return 0;
            return _weights.TryGetValue(signalName, out var w) ? w : 0;
        }
    }
}