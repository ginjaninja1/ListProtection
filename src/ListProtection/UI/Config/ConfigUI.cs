using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;

namespace ListProtection.UI.Config
{

    public class ConfigUI : EditableOptionsBase
    {
        public override string EditorTitle => "List Protection Settings";

        public override string EditorDescription => "Adjust background enforcement loops and backup parameters.";

        public CaptionItem GeneralHeading { get; set; } = new CaptionItem("System Diagnostics");

        [DisplayName("Hourly Engine Monitor Loop")]
        [Description("Run structural lock tasks cleanly across background cycles.")]
        public bool EnableSync { get; set; } = false;

        [DisplayName("Backup Destination Path")]
        [Description("Storage path where playlist configuration history logs are output.")]
        public string BackupPath { get; set; } = "C:\\Backups";
    }
}
