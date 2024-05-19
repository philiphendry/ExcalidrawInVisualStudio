using System;

namespace ExcalidrawInVisualStudio
{
    public static class GuidStrings
    {
        public const string GuidClientCmdSet = "BE690905-B0DD-4FF2-90B7-4473347CB1EA";
        public const string GuidEditorFactory = "E80D9338-BAFB-40BF-A3EB-D5D73839CAF0";
    }

    internal static class GuidList
    {
        public static readonly Guid guidEditorCmdSet = new Guid(GuidStrings.GuidClientCmdSet);
        public static readonly Guid guidEditorFactory = new Guid(GuidStrings.GuidEditorFactory);
    };
}