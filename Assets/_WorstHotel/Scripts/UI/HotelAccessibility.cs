using UnityEngine;
namespace WorstHotel
{
    public static class HotelAccessibility
    {
        public static bool Subtitles { get => PlayerPrefs.GetInt("Hotel.Subtitles", 1) != 0; set => Set("Hotel.Subtitles", value); }
        public static bool Prompts { get => PlayerPrefs.GetInt("Hotel.Prompts", 1) != 0; set => Set("Hotel.Prompts", value); }
        public static bool EnhancedLabels { get => PlayerPrefs.GetInt("Hotel.EnhancedLabels", 0) != 0; set => Set("Hotel.EnhancedLabels", value); }
        static void Set(string key, bool value) { PlayerPrefs.SetInt(key, value ? 1 : 0); PlayerPrefs.Save(); }
    }
}
