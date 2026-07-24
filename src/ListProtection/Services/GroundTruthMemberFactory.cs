using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using System.Collections.Generic;
using System.Linq;

namespace ListProtection.Services
{
    /// <summary>
    /// Single source of truth for constructing a GroundTruthMember from a live BaseItem.
    ///
    /// Three capture sites exist in the codebase:
    ///   PlaylistManagementPageView.CaptureMembers   — initial protect
    ///   PlaylistRepairService (rebuild path)         — after atomic remove/re-add
    ///   PlaylistRepairService (create-playlist path) — after CreatePlaylist
    ///
    /// Media-type-specific fields are populated only for known types. Unknown types
    /// receive base fields only — evidence collectors handle null fields gracefully.
    /// </summary>
    public static class GroundTruthMemberFactory
    {
        public static GroundTruthMember FromItem(BaseItem item)
        {
            var member = new GroundTruthMember
            {
                InternalId = item.InternalId,
                Id = item.Id.ToString("N"),
                Name = item.Name ?? string.Empty,
                Path = item.Path ?? string.Empty,
                ListItemEntryId = item.ListItemEntryId,
                MediaType = item.GetType().Name,
                RunTimeTicks = item.RunTimeTicks,
                ProductionYear = item.ProductionYear,
            };

            if (item is Audio audio)
                PopulateAudio(member, audio);
            else if (item is Episode episode)
                PopulateEpisode(member, episode);
            else if (item is Movie movie)
                PopulateMovie(member, movie);

            return member;
        }

        // ── Audio ──────────────────────────────────────────────────────────

        private static void PopulateAudio(GroundTruthMember member, Audio audio)
        {
            member.Album = audio.Album ?? string.Empty;
            member.AlbumArtist = GetFirstAlbumArtist(audio);
            member.Artists = GetArtists(audio);
            member.IndexNumber = audio.IndexNumber;

            member.MusicBrainzTrackId = GetProviderId(audio, MetadataProviders.MusicBrainzTrack);
        }

        private static string GetFirstAlbumArtist(Audio audio)
        {
            try
            {
                var artists = audio.AlbumArtists;
                return artists != null && artists.Length > 0 ? artists[0] : null;
            }
            catch { return null; }
        }

        private static List<string> GetArtists(Audio audio)
        {
            try
            {
                var artists = audio.Artists;
                if (artists == null || artists.Length == 0) return null;
                var list = artists.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
                return list.Count > 0 ? list : null;
            }
            catch { return null; }
        }

        // ── Episode ────────────────────────────────────────────────────────

        private static void PopulateEpisode(GroundTruthMember member, Episode episode)
        {
            member.SeriesName = episode.FindSeriesName();
            member.SeasonNumber = episode.ParentIndexNumber;  // ParentIndexNumber = season number
            member.IndexNumber = episode.IndexNumber;        // IndexNumber = episode number

            // Capture Series-level provider IDs — episode-level IDs are unreliable.
            var series = episode.GetSeries(null);
            if (series?.ProviderIds != null)
            {
                member.SeriesTvdbId = GetProviderIdFromDict(series.ProviderIds, MetadataProviders.Tvdb);
                member.SeriesImdbId = GetProviderIdFromDict(series.ProviderIds, MetadataProviders.Imdb);
            }
        }

        // ── Movie ──────────────────────────────────────────────────────────

        private static void PopulateMovie(GroundTruthMember member, Movie movie)
        {
            // ProductionYear already captured in base (item.ProductionYear above)
            member.ImdbId = GetProviderId(movie, MetadataProviders.Imdb);
            member.TmdbId = GetProviderId(movie, MetadataProviders.Tmdb);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string GetProviderId(BaseItem item, MetadataProviders provider)
        {
            try { return GetProviderIdFromDict(item.ProviderIds, provider); }
            catch { return null; }
        }

        private static string GetProviderIdFromDict(
            MediaBrowser.Model.Entities.ProviderIdDictionary ids,
            MetadataProviders provider)
        {
            if (ids == null) return null;
            try
            {
                var key = provider.ToString();
                return ids.TryGetValue(key, out var val) ? val : null;
            }
            catch { return null; }
        }
    }
}