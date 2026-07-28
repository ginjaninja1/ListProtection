using System.Collections.Generic;

namespace ListProtection.Storage
{
    public class GroundTruthMember
    {
        /// <summary>
        /// Emby internal ID (long). Fast for in-process lookup.
        /// </summary>
        public long InternalId { get; set; }

        /// <summary>
        /// Guid "N" format string. Durable identifier across restarts.
        /// </summary>
        public string Id { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

        /// <summary>
        /// Populated at capture time via GetItemList readback.
        /// Used to correlate PlaylistItemsRemoved events.
        /// </summary>
        public long ListItemEntryId { get; set; }

        // ── Media type discriminator ───────────────────────────────────────

        /// <summary>
        /// Emby type name at capture time: "Audio", "Episode", "Movie", etc.
        /// Null for legacy entries — evidence collectors apply base signals only.
        /// </summary>
        public string MediaType { get; set; }

        // ── Shared, generic across every type ───────────────────────────────

        /// <summary>
        /// item.RunTimeTicks at capture time. Used by Audio, Episode, Movie collectors
        /// for duration-tolerance matching. Null if absent.
        /// </summary>
        public long? RunTimeTicks { get; set; }

        /// <summary>
        /// item.ProductionYear at capture time. Used by Movie collector (Title+Year signal).
        /// Null if absent or not applicable.
        /// </summary>
        public int? ProductionYear { get; set; }

        /// <summary>
        /// item.IndexNumber at capture time. Generic across every type Emby uses this
        /// field for — track number (Audio), episode number (Episode), season number
        /// (Season). Captured once, unconditionally, for every item — not type-gated —
        /// so new member types automatically pick this up with no factory change.
        /// </summary>
        public int? IndexNumber { get; set; }

        /// <summary>
        /// Full raw ProviderIds dictionary for THIS item, captured unconditionally for
        /// every type at capture time. Key is the provider name exactly as Emby stores
        /// it (e.g. "Imdb", "Tmdb", "Tvdb", "MusicBrainzTrack", "MusicBrainzArtist",
        /// "MusicBrainzAlbum", "AudioDbArtist"). Case-insensitive, matching Emby's own
        /// ProviderIdDictionary. Never null — empty if the item has no provider IDs.
        ///
        /// This is the single generic identity-evidence source for every media type.
        /// There are deliberately no per-type convenience fields (no ImdbId, TmdbId,
        /// MusicBrainzTrackId, etc.) — a new type with a new provider ID scheme needs
        /// no factory change to be captured; evidence collectors just look up the key
        /// they care about.
        /// </summary>
        public Dictionary<string, string> ProviderIds { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        // ── Audio-specific fields ──────────────────────────────────────────

        /// <summary>
        /// item.Album at capture time. Null if not Audio or tag absent.
        /// </summary>
        public string Album { get; set; }

        /// <summary>
        /// item.AlbumArtists[0] at capture time. Null if not Audio or absent.
        /// </summary>
        public string AlbumArtist { get; set; }

        /// <summary>
        /// item.Artists at capture time. Null/empty if not Audio or tag absent.
        /// Artists[0] is the primary artist used for gate and scoring.
        /// </summary>
        public List<string> Artists { get; set; }

        // ── Episode-specific fields ────────────────────────────────────────

        /// <summary>
        /// Episode.SeriesName at capture time (episode's parent series name).
        /// Null if not Episode.
        /// </summary>
        public string SeriesName { get; set; }

        /// <summary>
        /// episode.ParentIndexNumber (season number) at capture time. Null if absent.
        /// </summary>
        public int? SeasonNumber { get; set; }

        /// <summary>
        /// Full raw ProviderIds dictionary of the EPISODE'S PARENT SERIES at capture
        /// time — deliberately distinct from <see cref="ProviderIds"/>, which holds
        /// the episode's own (unreliable) provider IDs. Sourced from
        /// Series.ProviderIds at capture time, since the live Series may not resolve
        /// the same way at discovery time. Null if not Episode or parent unresolved.
        /// </summary>
        public Dictionary<string, string> ParentSeriesProviderIds { get; set; }
    }
}