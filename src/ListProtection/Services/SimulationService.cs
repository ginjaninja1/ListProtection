using System;
using System.Collections.Generic;
using System.Linq;
using PlaylistProtection.Core.Confidence;
using PlaylistProtection.Core.Models;

namespace PlaylistProtection.Services
{
    /// <summary>
    /// MVP simulation harness for testing confidence engine behaviour
    /// without Emby runtime dependencies.
    /// </summary>
    public class SimulationService
    {
        private readonly ConfidenceEngine _engine;

        public SimulationService(ConfidenceEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// Simulates matching a missing item against a candidate set.
        /// Returns ranked results.
        /// </summary>
        public List<CandidateItem> RunMatchSimulation(
            MissingItem missing,
            List<CandidateItem> candidates)
        {
            if (missing == null)
                throw new ArgumentNullException(nameof(missing));

            if (candidates == null)
                return new List<CandidateItem>();

            return _engine.Evaluate(missing, candidates);
        }

        /// <summary>
        /// Creates a quick test scenario for debugging rules.
        /// </summary>
        public List<CandidateItem> CreateSampleCandidates(string baseName)
        {
            return new List<CandidateItem>
            {
                new CandidateItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = baseName,
                    Path = $"C:\\Media\\Music\\{baseName}.mp3",
                    MediaType = "Audio"
                },
                new CandidateItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = baseName + " (Live)",
                    Path = $"D:\\Archive\\{baseName} Live.flac",
                    MediaType = "Audio"
                },
                new CandidateItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Unrelated Track",
                    Path = "C:\\Media\\Music\\Other.mp3",
                    MediaType = "Audio"
                }
            };
        }

        /// <summary>
        /// Creates a sample missing item for testing.
        /// </summary>
        public MissingItem CreateSampleMissing(string name)
        {
            return new MissingItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Path = $"C:\\Media\\Music\\{name}.mp3",
                MediaType = "Audio",
                LastSeenUtc = DateTime.UtcNow,
                Source = "Simulation",
                WasConfirmedInLibrary = true
            };
        }

        /// <summary>
        /// Full end-to-end test helper.
        /// </summary>
        public CandidateItem RunFullSimulation(string name)
        {
            var missing = CreateSampleMissing(name);
            var candidates = CreateSampleCandidates(name);

            var results = RunMatchSimulation(missing, candidates);

            return results.FirstOrDefault();
        }
    }
}