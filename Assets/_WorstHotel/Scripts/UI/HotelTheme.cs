using UnityEngine;

namespace WorstHotel
{
    /// <summary>A small parchment/ink/brass presentation vocabulary for the reception ledger.</summary>
    public static class HotelTheme
    {
        public static readonly Color Paper = new Color(.94f, .88f, .73f);
        public static readonly Color LightPaper = new Color(1, .96f, .85f);
        public static readonly Color Ink = new Color(.18f, .21f, .19f);
        public static readonly Color Wine = new Color(.37f, .10f, .15f);
        public static readonly Color Brass = new Color(.72f, .53f, .25f);
        public static readonly Color Teal = new Color(.16f, .36f, .33f);
        public static readonly Color Muted = new Color(.42f, .43f, .36f);
        public static readonly Color Red = new Color(.65f, .18f, .10f);
        public static GUIStyle Title, Heading, Body, Small, Button, Center;

        public static void Ensure()
        {
            if (Title != null) return;
            Title = Style(38, FontStyle.Bold);
            Heading = Style(23, FontStyle.Bold);
            Body = Style(18, FontStyle.Normal);
            Small = Style(15, FontStyle.Normal);
            Center = Style(18, FontStyle.Bold); Center.alignment = TextAnchor.MiddleCenter;
            Button = Style(17, FontStyle.Bold); Button.alignment = TextAnchor.MiddleCenter;
            Button.padding = new RectOffset(10, 10, 5, 5);
        }

        static GUIStyle Style(int size, FontStyle weight)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size, fontStyle = weight, wordWrap = true,
                clipping = TextClipping.Clip, richText = false
            };
            style.normal.textColor = Ink;
            return style;
        }

        public static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }

        public static void Border(Rect rect, Color color, float width = 2)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, width), color);
            Fill(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            Fill(new Rect(rect.x, rect.y, width, rect.height), color);
            Fill(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        public static void Label(Rect rect, string text, GUIStyle style = null, Color? color = null)
        {
            Ensure(); style = style ?? Body;
            var previous = style.normal.textColor;
            style.normal.textColor = color ?? Ink;
            GUI.Label(rect, text, style);
            style.normal.textColor = previous;
        }

        public static void Meter(Rect rect, float normalized, Color color)
        {
            Fill(rect, new Color(.25f, .23f, .20f, .14f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(normalized), rect.height), color);
        }
    }
}
