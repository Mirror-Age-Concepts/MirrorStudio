using System.Text.Json;

namespace MirrorStudio.Project
{
    public class RecentProjectsService
    {
        private static string ConfigDir =>
            System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "MirrorStudio");

        private static string FilePath => System.IO.Path.Combine(ConfigDir, "recents.json");

        public System.Collections.Generic.List<string> Load()
        {
            try
            {
                if (System.IO.File.Exists(FilePath))
                {
                    return JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(System.IO.File.ReadAllText(FilePath))
                           ?? new System.Collections.Generic.List<string>();
                }
            }
            catch
            {
            }
            return new System.Collections.Generic.List<string>();
        }

        public void Add(string projectFile)
        {
            if (string.IsNullOrWhiteSpace(projectFile)) return;
            var list = Load();
            list.RemoveAll(p => string.Equals(p, projectFile, System.StringComparison.OrdinalIgnoreCase));
            list.Insert(0, projectFile);
            while (list.Count > 10) list.RemoveAt(list.Count - 1);
            Save(list);
        }

        public void Remove(string projectFile)
        {
            var list = Load();
            list.RemoveAll(p => string.Equals(p, projectFile, System.StringComparison.OrdinalIgnoreCase));
            Save(list);
        }

        public void PurgeMissing()
        {
            var list = Load();
            int before = list.Count;
            list.RemoveAll(p => !System.IO.File.Exists(p));
            if (list.Count != before) Save(list);
        }

        private void Save(System.Collections.Generic.List<string> list)
        {
            try
            {
                System.IO.Directory.CreateDirectory(ConfigDir);
                System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }
    }
}
