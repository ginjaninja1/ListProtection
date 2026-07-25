namespace ListProtection.Scoring
{
    /// <summary>
    /// A single atomic boolean signal produced by an IEvidenceCollector.
    /// A fact is either present (fired) or absent.
    /// Facts are named constants — the scorer maps names to weights via rule table.
    /// Collectors never decide what a fact is worth or suppress other facts.
    /// </summary>
    public sealed class EvidenceFact
    {
        public string SignalName { get; }

        public EvidenceFact(string signalName)
        {
            SignalName = signalName;
        }
    }
}