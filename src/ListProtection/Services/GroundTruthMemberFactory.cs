using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using System.Collections.Generic;
using System.Linq;

namespace ListProtection.Services
{
    /// <summary>
    /// Single source of truth for constructing a GroundTruthMember from a live BaseItem.
    ///
    /// Three capture sites exist in the codebase:
    ///   PlaylistManagementPageView.CaptureMembers   — initial protect
    ///   ListRepairService (rebuild path)         — after atomic remove/re-add
    ///   ListRepairService (create-playlist path) — after CreatePlaylist
    ///
    /// Identity capture is generic wherever Emby exposes it generically: IndexNumber
    /// and ProviderIds are base BaseItem members, so they are captured unconditionally
    /// for every item, regardless of type. Only fields that genuinely differ in
    /// meaning per type (Album/AlbumArtist/Artists for Audio, SeriesName/SeasonNumber/
    /// ParentSeriesProviderIds for Episode) are populated behind a type check.
    /// A new member type therefore needs no factory change to get base identity
    /// evidence (Name, Path, ProviderIds, IndexNumber) captured — only genuinely new
    /// signal types require new fields and a new branch here.
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
                IndexNumber = item.IndexNumber,
                ProviderIds = CaptureProviderIds(item),
            };

            if (item is Audio audio)
                PopulateAudio(member, audio);
            else if (item is Episode episode)
                PopulateEpisode(member, episode);

            return member;
        }

        // ── Audio ──────────────────────────────────────────────────────────

        private static void PopulateAudio(GroundTruthMember member, Audio audio)
        {
            member.Album = audio.Album ?? string.Empty;
            member.AlbumArtist = GetFirstAlbumArtist(audio);
            member.Artists = GetArtists(audio);
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
            // IndexNumber (episode number) already captured generically above.

            // Capture the PARENT SERIES' full ProviderIds — episode-level provider IDs
            // are unreliable, so this is intentionally separate from member.ProviderIds
            // (which holds the episode's own, already captured above).
            var series = episode.GetSeries(null);
            if (series != null)
                member.ParentSeriesProviderIds = CaptureProviderIds(series);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Captures the full raw ProviderIds dictionary for any item, generically.
        /// Never returns null — an item with no provider IDs yields an empty dict,
        /// same as one whose ProviderIds property throws or is itself null.
        /// </summary>
        private static Dictionary<string, string> CaptureProviderIds(BaseItem item)
        {
            var result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            try
            {
                var ids = item.ProviderIds;
                if (ids == null) return result;

                foreach (var kvp in ids)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                        result[kvp.Key] = kvp.Value;
                }
            }
            catch { /* leave result empty */ }

            return result;
        }
    }
}