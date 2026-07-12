using PlaylistProtection.Core.Confidence;
using PlaylistProtection.Services;

namespace PlaylistProtection
{
    /// <summary>
    /// Simple runtime service registry for MVP plugin bootstrap.
    /// Used by PluginEntryPoint to initialise and share core services.
    /// </summary>
    public static class PluginServices
    {
        /// <summary>
        /// Core confidence evaluation engine (pure domain logic).
        /// </summary>
        public static ConfidenceEngine ConfidenceEngine { get; set; }

        /// <summary>
        /// Optional simulation/testing service for rule evaluation.
        /// </summary>
        public static SimulationService SimulationService { get; set; }

        /// <summary>
        /// Indicates whether core services are ready.
        /// </summary>
        public static bool IsInitialised =>
            ConfidenceEngine != null;
    }
}