using ListProtection.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
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
    /// All three must populate identical fields, including the new MediaType and
    /// audio-specific fields added in the evidence/scoring architecture refactor.
    /// Using this factory keeps them in sync.
    ///
    /// Audio fields (Album, AlbumArtist, Artists, IndexNumber, RunTimeTicks) are
    /// populated only when the item is an Audio instance. For all other types they
    /// remain null — evidence collectors skip null fields rather than treating
    /// absence as a mismatch.
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
                MediaType = item.GetType().Name
            };

            if (item is Audio audio)
            {
                member.Album = audio.Album ?? string.Empty;
                member.AlbumArtist = GetFirstAlbumArtist(audio);
                member.Artists = GetArtists(audio);
                member.IndexNumber = audio.IndexNumber;
                member.RunTimeTicks = audio.RunTimeTicks;
            }

            return member;
        }

        private static string GetFirstAlbumArtist(Audio audio)
        {
            try
            {
                var artists = audio.AlbumArtists;
                if (artists == null || artists.Length == 0) return null;
                return artists[0];
            }
            catch { return null; }
        }

        private static List<string> GetArtists(Audio audio)
        {
            try
            {
                var artists = audio.Artists;
                if (artists == null || artists.Length == 0) return null;
                var list = artists
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();
                return list.Count > 0 ? list : null;
            }
            catch { return null; }
        }
    }
}