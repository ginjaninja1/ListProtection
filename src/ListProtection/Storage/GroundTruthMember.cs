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

        // ── Shared ────────────────────────────────────────────────────────

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

        /// <summary>
        /// item.IndexNumber (track number) at capture time. Null if absent.
        /// </summary>
        public int? IndexNumber { get; set; }

        /// <summary>
        /// MusicBrainz Track ID at capture time. Null if absent.
        /// Sourced from item.ProviderIds[MetadataProviders.MusicBrainzTrack].
        /// </summary>
        public string MusicBrainzTrackId { get; set; }

        // ── Episode-specific fields ────────────────────────────────────────

        /// <summary>
        /// Episode.SeriesName at capture time. Null if not Episode.
        /// </summary>
        public string SeriesName { get; set; }

        /// <summary>
        /// episode.ParentIndexNumber (season number) at capture time. Null if absent.
        /// </summary>
        public int? SeasonNumber { get; set; }

        // IndexNumber reused for episode number — already present above.
        // SeasonNumber = ParentIndexNumber, EpisodeNumber = IndexNumber.

        /// <summary>
        /// TVDB ID of the parent Series at capture time. Null if absent.
        /// Sourced from Series.ProviderIds["Tvdb"] at capture time.
        /// Stored here because the live Series may not be resolved at discovery time.
        /// </summary>
        public string SeriesTvdbId { get; set; }

        /// <summary>
        /// IMDB ID of the parent Series at capture time. Null if absent.
        /// </summary>
        public string SeriesImdbId { get; set; }

        // ── Movie-specific fields ──────────────────────────────────────────

        /// <summary>
        /// IMDB ID at capture time. Null if absent.
        /// Sourced from item.ProviderIds["Imdb"].
        /// </summary>
        public string ImdbId { get; set; }

        /// <summary>
        /// TMDB ID at capture time. Null if absent.
        /// Sourced from item.ProviderIds["Tmdb"].
        /// </summary>
        public string TmdbId { get; set; }
    }
}