using ListProtection.Services;
using ListProtection.Storage;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using System;

namespace ListProtection.EntryPoints
{
    /// <summary>
    /// Production IServerEntryPoint — keeps ground truth in sync as collection
    /// (BoxSet) membership changes via Emby events.
    ///
    /// PROVEN (via ILSpy dump of ICollectionManager, MediaBrowser.Controller
    /// 4.10.0.20):
    ///   event EventHandler&lt;CollectionModifiedEventArgs&gt; ItemsAddedToCollection;
    ///   event EventHandler&lt;CollectionModifiedEventArgs&gt; ItemsRemovedFromCollection;
    ///   CollectionModifiedEventArgs.Collection   — BoxSet, directly
    ///   CollectionModifiedEventArgs.ItemsChanged — IList&lt;long&gt; InternalIds, directly
    ///
    /// Unlike the playlist add flow, there is no two-event dance here — both
    /// events carry complete information at fire time, so no pending/readback
    /// queue is needed. This also matches the item-side membership model already
    /// established in Evidence.md (collections have no ListItemEntryId concept —
    /// they're unordered sets of InternalIds).
    ///
    /// Repair suppression reuses Plugin.RepairSuppressedLists (keyed by list
    /// InternalId — shared across playlists and collections).
    ///
    /// Stores are accessed via ListProtectionPlugin.Instance (singleton on Plugin.cs).
    /// </summary>
    public class CollectionMaintenanceService : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly ILogger _logger;

        public CollectionMaintenanceService(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _collectionManager = collectionManager;
            _logger = logManager.GetLogger(nameof(CollectionMaintenanceService));
        }

        public void Run()
        {
            _collectionManager.ItemsAddedToCollection += OnItemsAddedToCollection;
            _collectionManager.ItemsRemovedFromCollection += OnItemsRemovedFromCollection;

            _logger.Info("[CollectionMaintenanceService] Subscribed to collection events");
        }

        // ── ItemsAddedToCollection ───────────────────────────────────────────

        private void OnItemsAddedToCollection(object sender, CollectionModifiedEventArgs e)
        {
            var collection = e.Collection;

            if (collection == null || e.ItemsChanged == null || e.ItemsChanged.Count == 0)
                return;

            var collectionIdN = collection.Id.ToString("N");

            if (!IsProtected(collectionIdN))
                return;

            var plugin = ListProtectionPlugin.Instance;
            if (plugin != null && plugin.RepairSuppressedLists.ContainsKey(collection.InternalId))
            {
                _logger.Warn(
                    "[CollectionMaintenanceService] ItemsAddedToCollection — repair in progress for '{0}' ({1}) — skipping (repair owns GT update)",
                    collection.Name ?? "(null)",
                    collectionIdN);
                return;
            }

            _logger.Info(
                "[CollectionMaintenanceService] ItemsAddedToCollection — protected collection '{0}' ({1}) | {2} item(s)",
                collection.Name ?? "(null)",
                collectionIdN,
                e.ItemsChanged.Count);

            try
            {
                plugin.WriterLock.Wait();
                try
                {
                    var entries = plugin.GroundTruthStore.Load();

                    if (!entries.TryGetValue(collectionIdN, out var entry))
                    {
                        _logger.Warn(
                            "[CollectionMaintenanceService] No ground truth entry for collection {0} — skipping add",
                            collectionIdN);
                        return;
                    }

                    var added = 0;

                    foreach (var internalId in e.ItemsChanged)
                    {
                        var alreadyPresent = false;
                        foreach (var existing in entry.Members)
                        {
                            if (existing.InternalId == internalId)
                            {
                                alreadyPresent = true;
                                break;
                            }
                        }

                        if (alreadyPresent)
                        {
                            _logger.Info(
                                "[CollectionMaintenanceService] Member InternalId={0} already in ground truth for collection {1} — skipping",
                                internalId,
                                collectionIdN);
                            continue;
                        }

                        var item = _libraryManager.GetItemById(internalId);
                        if (item == null)
                        {
                            _logger.Warn(
                                "[CollectionMaintenanceService] GetItemById({0}) returned null — cannot capture member for collection {1}",
                                internalId,
                                collectionIdN);
                            continue;
                        }

                        var member = GroundTruthMemberFactory.FromItem(item);
                        entry.Members.Add(member);

                        _logger.Info(
                            "[CollectionMaintenanceService] Added member '{0}' | InternalId={1} | collection={2}",
                            item.Name ?? "(null)",
                            internalId,
                            collectionIdN);

                        added++;
                    }

                    if (added > 0)
                    {
                        plugin.GroundTruthStore.Save(entries);
                        _logger.Info(
                            "[CollectionMaintenanceService] Saved {0} new member(s) to ground truth for collection {1}",
                            added,
                            collectionIdN);
                    }
                    else
                    {
                        _logger.Info(
                            "[CollectionMaintenanceService] No new members added for collection {0} — store unchanged",
                            collectionIdN);
                    }
                }
                finally
                {
                    plugin.WriterLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[CollectionMaintenanceService] Add failed", ex);
            }
        }

        // ── ItemsRemovedFromCollection ───────────────────────────────────────

        private void OnItemsRemovedFromCollection(object sender, CollectionModifiedEventArgs e)
        {
            var collection = e.Collection;

            if (collection == null || e.ItemsChanged == null || e.ItemsChanged.Count == 0)
                return;

            var collectionIdN = collection.Id.ToString("N");

            if (!IsProtected(collectionIdN))
                return;

            var plugin = ListProtectionPlugin.Instance;
            if (plugin != null && plugin.RepairSuppressedLists.ContainsKey(collection.InternalId))
            {
                _logger.Warn(
                    "[CollectionMaintenanceService] ItemsRemovedFromCollection — repair in progress for '{0}' ({1}) — skipping (repair owns GT update)",
                    collection.Name ?? "(null)",
                    collectionIdN);
                return;
            }

            _logger.Info(
                "[CollectionMaintenanceService] ItemsRemovedFromCollection — protected collection '{0}' ({1}) | {2} item(s)",
                collection.Name ?? "(null)",
                collectionIdN,
                e.ItemsChanged.Count);

            try
            {
                plugin.WriterLock.Wait();
                try
                {
                    var entries = plugin.GroundTruthStore.Load();

                    if (!entries.TryGetValue(collectionIdN, out var entry))
                    {
                        _logger.Warn(
                            "[CollectionMaintenanceService] No ground truth entry for collection {0} — skipping remove",
                            collectionIdN);
                        return;
                    }

                    var removed = 0;

                    foreach (var internalId in e.ItemsChanged)
                    {
                        for (var i = entry.Members.Count - 1; i >= 0; i--)
                        {
                            if (entry.Members[i].InternalId != internalId)
                                continue;

                            _logger.Info(
                                "[CollectionMaintenanceService] Removing member '{0}' | InternalId={1} | collection={2}",
                                entry.Members[i].Name ?? "(null)",
                                internalId,
                                collectionIdN);

                            entry.Members.RemoveAt(i);
                            removed++;
                            break; // InternalId is unique — stop after first match
                        }
                    }

                    if (removed > 0)
                    {
                        plugin.GroundTruthStore.Save(entries);
                        _logger.Info(
                            "[CollectionMaintenanceService] Removed {0} member(s) from ground truth for collection {1}",
                            removed,
                            collectionIdN);
                    }
                    else
                    {
                        _logger.Warn(
                            "[CollectionMaintenanceService] ItemsRemovedFromCollection fired but no matching InternalIds found in ground truth for collection {0} — store unchanged",
                            collectionIdN);
                    }
                }
                finally
                {
                    plugin.WriterLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[CollectionMaintenanceService] Remove failed", ex);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private bool IsProtected(string collectionIdN)
        {
            var plugin = ListProtectionPlugin.Instance;
            if (plugin == null) return false;

            var protectedIds = plugin.ListStore.Load();
            return protectedIds.Contains(collectionIdN);
        }

        // ── Cleanup ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _collectionManager.ItemsAddedToCollection -= OnItemsAddedToCollection;
            _collectionManager.ItemsRemovedFromCollection -= OnItemsRemovedFromCollection;

            _logger.Info("[CollectionMaintenanceService] Disposed — unsubscribed from all events");
        }
    }
}