namespace PlaylistProtection.Data.Entities
{
    /// <summary>
    /// Represents a resolved failure state for an item expected in a ProtectedList.
    /// This is equivalent to MissingItem in the evaluation pipeline.
    /// </summary>
    public class MissingMember
    {
        public string Id { get; }
        public string ExpectedName { get; }
        public string ExpectedPath { get; }
        public string SourceListId { get; }

        public MissingMember(string id, string expectedName, string expectedPath, string sourceListId)
        {
            Id = id;
            ExpectedName = expectedName;
            ExpectedPath = expectedPath;
            SourceListId = sourceListId;
        }
    }
}