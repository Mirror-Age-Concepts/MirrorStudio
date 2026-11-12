namespace MirrorStudio.Graphics.Models;

public class TextLayer : GraphicLayer
{
    public string Text { get; set; } = "Text";
    public TextStyle Style { get; set; } = new();
}
