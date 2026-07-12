using Emby.Web.GenericEdit;
using System;

namespace ListProtection.UIBaseClasses.Store
{
    public class FileSavedEventArgs : EventArgs
    {
        public FileSavedEventArgs(EditableOptionsBase options)
        {
            this.Options = options;
        }

        public EditableOptionsBase Options { get; }
    }
}
