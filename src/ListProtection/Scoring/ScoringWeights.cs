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
    /// Auto-repair for Audio is governed by a hard gate (AudioAutoRepairEligibility),
    /// NOT by this score. The score is used solely to rank candidates for the user
    /// and to select the best candidate when multiple pass the gate.
    ///
    /// Weight philosophy (for ranking purposes only):
    ///    50+  = strong identity signal — decisive on its own
    ///    20–49 = useful corroborating signal
    ///    1–19  = weak contextual hint
    ///
    /// ── BaseItem signals (all media types) ────────────────────────────────
    ///
    ///   FilenameStemExact      25  Stem without extension, case-insensitive exact
    ///   FilenameStemNormalized 15  After stripping track-number prefix ("02. ")
    ///   NameExact              40  item.Name exact, case-insensitive
    ///   NameNormalized         20  whitespace-collapsed lowercase
    ///   ParentFolderMatch      15  Immediate parent folder name
    ///   GrandparentFolderMatch 10  Two levels up (artist folder for music)
    ///
    /// ── Audio-specific signals ─────────────────────────────────────────────
    ///
    ///   ArtistExact            50  GT Artists[0] is exact member of candidate.Artists
    ///   AlbumExact             40  item.Album exact, case-insensitive
    ///   AlbumArtistExact       20  item.AlbumArtists[0] exact, case-insensitive
    ///   IndexOnSameAlbum       30  track number matches AND album matches
    ///   IndexOnDifferentAlbum  10  track number matches, album differs
    ///   DurationMatch           5  |RunTimeTicks delta| within 3-second tolerance
    ///
    /// Example scores:
    ///   Same track, same artist, same album, folder renamed:
    ///     ArtistExact(50) + AlbumExact(40) + NameExact(40) + IndexOnSameAlbum(30)
    ///     + GrandparentFolderMatch(10) = 170  [gate passes → auto-repairs]
    ///
    ///   Same track, same artist, compilation instead of studio album:
    ///     ArtistExact(50) + NameExact(40) + IndexOnDifferentAlbum(10)
    ///     + GrandparentFolderMatch(10) = 110  [gate fails → surfaces to user]
    ///
    ///   Wrong artist, same track name (Jessie Ware - Swan Song):
    ///     Never reaches scoring — filtered by artist gate in CandidateDiscoverer.
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

        // ── Audio-specific signals ─────────────────────────────────────────

        public const int ArtistExact = 50;
        public const int AlbumExact = 40;
        public const int AlbumArtistExact = 20;
        public const int IndexOnSameAlbum = 30;
        public const int IndexOnDifferentAlbum = 10;
        public const int DurationMatch = 5;

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
                { nameof(ArtistExact),            ArtistExact            },
                { nameof(AlbumExact),             AlbumExact             },
                { nameof(AlbumArtistExact),       AlbumArtistExact       },
                { nameof(IndexOnSameAlbum),       IndexOnSameAlbum       },
                { nameof(IndexOnDifferentAlbum),  IndexOnDifferentAlbum  },
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