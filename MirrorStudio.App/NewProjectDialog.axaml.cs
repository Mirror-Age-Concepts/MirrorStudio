using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace MirrorStudio.App
{
    public partial class NewProjectDialog : Window
    {
        public bool WasConfirmed { get; private set; }
        public string ProjectName { get; private set; } = string.Empty;
        public int Width { get; private set; } = 1920;
        public int Height { get; private set; } = 1080;
        public double FrameRate { get; private set; } = 25.0;
        public string ProjectPath { get; private set; } = string.Empty;

        public NewProjectDialog()
        {
            InitializeComponent();
            // Set defaults
            NameBox.Text = "New Project";
            WidthBox.Text = "1920";
            HeightBox.Text = "1080";
            FpsBox.SelectedIndex = 1; // 25
            PathBox.Text = System.IO.Path.Combine(
                MirrorStudio.Project.ProjectService.ProjectsRoot,
                Sanitize(NameBox.Text));
            NameBox.TextChanged += (_, _) =>
                PathBox.Text = System.IO.Path.Combine(
                    MirrorStudio.Project.ProjectService.ProjectsRoot,
                    Sanitize(NameBox.Text ?? ""));
        }

        private static string Sanitize(string s)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrWhiteSpace(s) ? "New Project" : s.Trim();
        }

        private async void OnBrowse(object? sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select Project Folder"
            };

            var result = await dlg.ShowAsync(this);
            if (!string.IsNullOrWhiteSpace(result))
            {
                PathBox.Text = result;
            }
        }

        private void OnCancel(object? sender, RoutedEventArgs e)
        {
            WasConfirmed = false;
            Close();
        }

        private async void OnOk(object? sender, RoutedEventArgs e)
        {
            ProjectName = NameBox.Text ?? "New Project";

            if (int.TryParse(WidthBox.Text, out var w)) Width = w;
            if (int.TryParse(HeightBox.Text, out var h)) Height = h;

            if (FpsBox.SelectedItem is ComboBoxItem item && double.TryParse(item.Content?.ToString(), out var f))
            {
                FrameRate = f;
            }

            ProjectPath = PathBox.Text ?? string.Empty;

            // validation
            if (Width <= 0 || Height <= 0)
            {
                await MessageBox.Show(this, "Validation error", "Resolution must be positive values.");
                return;
            }

            if (FrameRate <= 0)
            {
                await MessageBox.Show(this, "Validation error", "Frame rate must be a positive value.");
                return;
            }

            if (string.IsNullOrWhiteSpace(ProjectPath))
            {
                await MessageBox.Show(this, "Validation error", "Please select a project folder.");
                return;
            }

            try
            {
                System.IO.Directory.CreateDirectory(ProjectPath);
                // test write
                var testFile = System.IO.Path.Combine(ProjectPath, ".write_test");
                await System.IO.File.WriteAllTextAsync(testFile, "test");
                System.IO.File.Delete(testFile);
            }
            catch (System.Exception ex)
            {
                await MessageBox.Show(this, "Validation error", "Cannot write to selected folder: " + ex.Message);
                return;
            }

            WasConfirmed = true;
            Close();
        }
    }
}
