using System;
using System.IO;
using System.Text.Json;

namespace CavaWin
{
    public sealed class Config
    {
        public int Bars { get; set; } = 48;
        public int Framerate { get; set; } = 60;
        public double Gravity { get; set; } = 1.0;
        public double Sensitivity { get; set; } = 1.0;
        public double Smoothing { get; set; } = 1.4;
        public bool Peaks { get; set; } = true;
        public double Opacity { get; set; } = 0.9;
        public bool Topmost { get; set; } = true;
        public bool ClickThrough { get; set; } = false;
        public int Width { get; set; } = 560;
        public int Height { get; set; } = 240;
        public int Margin { get; set; } = 6;
        public int MinFreq { get; set; } = 50;
        public int MaxFreq { get; set; } = 10000;
        public string ColorFrom { get; set; } = "#2BFF9B";
        public string ColorTo { get; set; } = "#4D7CFF";
        public string Theme { get; set; } = "cappuccino";
        public string Mode { get; set; } = "bars"; // стиль: bars|monstercat|mirror|wave|led
        public int WindowLeft { get; set; } = -1;
        public int WindowTop { get; set; } = -1;

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CavaWin", "config.json");

        public static Config Load()
        {
            try
            {
                string json = null;
                if (File.Exists(DefaultPath))
                    json = File.ReadAllText(DefaultPath);
                else
                {
                    var near = Path.Combine(AppContext.BaseDirectory, "config.json");
                    if (File.Exists(near)) json = File.ReadAllText(near);
                }
                if (json != null)
                    return JsonSerializer.Deserialize<Config>(json, JsonOpts()) ?? new Config();
            }
            catch { }
            return new Config();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath));
                File.WriteAllText(DefaultPath, JsonSerializer.Serialize(this, JsonOpts()));
            }
            catch (Exception ex) { Program.Log("config save: " + ex.Message); }
        }

        private static JsonSerializerOptions JsonOpts() => new JsonSerializerOptions { WriteIndented = true };
    }
}