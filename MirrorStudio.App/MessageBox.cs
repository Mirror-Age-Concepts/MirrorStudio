using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.Threading.Tasks;

namespace MirrorStudio.App
{
    public static class MessageBox
    {
        public static async Task Show(Window owner, string title, string message)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(10) }
                }
            };

            await dlg.ShowDialog(owner);
        }
    }
}
