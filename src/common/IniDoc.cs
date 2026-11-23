using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoCHD
{
    /// <summary>
    /// Minimal INI editor that keeps comments and order, so CoCHD.ini stays readable after the
    /// launcher touched it.
    /// </summary>
    class IniDoc
    {
        readonly List<string> lines;
        readonly string path;

        IniDoc(string path, List<string> lines)
        {
            this.path = path;
            this.lines = lines;
        }

        public static IniDoc Load(string path)
        {
            var lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path, Encoding.Default)) : new List<string>();
            return new IniDoc(path, lines);
        }

        public string Get(string section, string key, string fallback = null)
        {
            int i = Find(section, key);
            if (i < 0)
                return fallback;
            var line = lines[i];
            return line.Substring(line.IndexOf('=') + 1).Trim();
        }

        public void Set(string section, string key, string value)
        {
            int i = Find(section, key);
            if (i >= 0)
            {
                var line = lines[i];
                int eq = line.IndexOf('=');
                string lead = line.Substring(0, eq + 1);
                bool spaced = eq + 1 < line.Length && line[eq + 1] == ' ';
                lines[i] = lead + (spaced ? " " : "") + value;
                return;
            }
            int sec = FindSection(section);
            if (sec < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
                    lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
                return;
            }
            int insert = sec + 1;
            while (insert < lines.Count && !lines[insert].TrimStart().StartsWith("["))
                insert++;
            while (insert > sec + 1 && lines[insert - 1].Trim().Length == 0)
                insert--;
            lines.Insert(insert, key + "=" + value);
        }

        public void Save()
        {
            var text = string.Join("\r\n", lines) + "\r\n";
            FileUtil.WriteAtomic(path, Encoding.Default.GetBytes(text));
        }

        int FindSection(string section)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]") &&
                    string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        int Find(string section, string key)
        {
            int sec = FindSection(section);
            if (sec < 0)
                return -1;
            for (int i = sec + 1; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("["))
                    break;
                if (t.StartsWith(";") || t.StartsWith("#"))
                    continue;
                int eq = t.IndexOf('=');
                if (eq > 0 && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }

    static class FileUtil
    {
        /// <summary>Write via a temporary file, so a crash never leaves a half-written file behind.</summary>
        public static void WriteAtomic(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }
}
