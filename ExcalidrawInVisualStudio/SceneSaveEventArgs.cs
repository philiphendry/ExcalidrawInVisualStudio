namespace ExcalidrawInVisualStudio;

public class SceneSaveEventArgs : EventArgs
{
    public string ContentType { get; set; }
    public byte[] Data { get; set; }
}