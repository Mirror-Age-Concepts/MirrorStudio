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

        /// <summary>Yes/No question. True when the user confirms.</summary>
        public static async Task<bool> ShowConfirm(Window owner, string title, string message)
        {
            bool confirmed = false;
            Window dlg = new()
            {
                Title = title,
                Width = 420,
                Height = 190,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var yesButton = new Button { Content = "Yes", Width = 84 };
            var noButton = new Button { Content = "No", Width = 84 };
            yesButton.Click += (_, _) => { confirmed = true; dlg.Close(); };
            noButton.Click += (_, _) => dlg.Close();

            dlg.Content = new StackPanel
            {
                Margin = new Thickness(14),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { yesButton, noButton }
                    }
                }
            };

            await dlg.ShowDialog(owner);
            return confirmed;
        }
    }
}
