using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;

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
    /// All three must populate identical fields, including the new MediaType and
    /// audio-specific fields added in the evidence/scoring architecture refactor.
    /// Using this factory keeps them in sync.
    ///
    /// Audio fields (Album, AlbumArtist, IndexNumber, RunTimeTicks) are populated
    /// only when the item is an Audio instance. For all other types they remain null,
    /// which is the correct sentinel — evidence collectors skip null fields rather
    /// than treating absence as a mismatch.
    /// </summary>
    public static class GroundTruthMemberFactory
    {
        /// <summary>
        /// Constructs a GroundTruthMember from a live BaseItem,
        /// including MediaType and any type-specific metadata fields.
        /// </summary>
        public static GroundTruthMember FromItem(BaseItem item)
        {
            var member = new GroundTruthMember
            {
                InternalId = item.InternalId,
                Id = item.Id.ToString("N"),
                Name = item.Name ?? string.Empty,
                Path = item.Path ?? string.Empty,
                ListItemEntryId = item.ListItemEntryId,
                MediaType = item.GetType().Name  // "Audio", "Episode", "Movie", etc.
            };

            // ── Audio-specific fields ──────────────────────────────────────

            if (item is Audio audio)
            {
                member.Album = audio.Album ?? string.Empty;
                member.AlbumArtist = GetFirstAlbumArtist(audio);
                member.IndexNumber = audio.IndexNumber;
                member.RunTimeTicks = audio.RunTimeTicks;
            }

            return member;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string GetFirstAlbumArtist(Audio audio)
        {
            try
            {
                var artists = audio.AlbumArtists;
                if (artists == null || artists.Length == 0) return null;
                return artists[0];
            }
            catch
            {
                return null;
            }
        }
    }
}