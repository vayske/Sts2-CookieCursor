using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using HarmonyLib;

namespace CookieCursor
{
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
    public static class ReplaceLocalCursor
    {
        static void Postfix(ref RunState __result)
        {
            var cursorManager = NGame.Instance?.CursorManager;
            var runManager = RunManager.Instance;
            var state = __result;
            if (cursorManager != null && runManager.IsInProgress)
            {
                if (state != null)
                {
                    foreach(Player player in state.Players)
                    {
                        string character = player.Character.GetType().Name.ToLower();
                        ModEntry.PlayersCharacter[player.NetId] = character;
                        if (ModEntry.RemoteCursor.ContainsKey(player.NetId))
                        {
                            ModEntry.ApplyRemoteCursor(player.NetId);
                        } 
                        else
                        {                    
                            ModEntry.LocalId = player.NetId;
                            ModEntry.ApplyLocalCursor(ModEntry.PlayersCharacter[player.NetId], cursorManager);
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(NRemoteMouseCursor), nameof(NRemoteMouseCursor.Create))]
    public static class RelaceRemoteCursor
    {
        static void Postfix(ulong playerId, NRemoteMouseCursor __result)
        {
            ModEntry.RemoteCursor[playerId] = __result;
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    public static class RevertCursor
    {
        static void Postfix()
        {
            var cursorManager = NGame.Instance?.CursorManager;
            if (cursorManager != null)
            {
                ModEntry.RevertLocalCursor(cursorManager);
            }
            ModEntry.PlayersCharacter.Clear();
            ModEntry.RemoteCursor.Clear();
        }
    }

    [ModInitializer("Initialize")]
    public class ModEntry
    {
        public static Dictionary<ulong, string> PlayersCharacter { get; set; } = new Dictionary<ulong, string>();
        public static Dictionary<ulong, NRemoteMouseCursor> RemoteCursor { get; set; } = new Dictionary<ulong, NRemoteMouseCursor>();
        public static ulong LocalId;
        public static Image? defaultCursorNotTilted;
        public static Image? defaultCursorTilted;
        
        public static void Initialize()
        {
            var harmony = new Harmony("com.sincel.cookiecursor");
            harmony.PatchAll();
            GD.Print("[CookieCursor] Mod initialized successfully.");
        }

        public static void ApplyLocalCursor(string character, NCursorManager cursorManager)
        {
            (Image notTilted, Image tilted) = GetCharCookies(character);
            if (notTilted == null || tilted == null)
            {
                GD.PrintErr($"[CookieCursor] Failed to apply local cursor. Assets for '{character}' missing.");
                return;
            }

            var notTiltedField = AccessTools.Field(typeof(NCursorManager), "_cursorNotTilted");
            var tiltedField = AccessTools.Field(typeof(NCursorManager), "_cursorTilted");

            defaultCursorNotTilted = (Image)notTiltedField.GetValue(cursorManager)!;
            defaultCursorTilted = (Image)tiltedField.GetValue(cursorManager)!;
            notTiltedField.SetValue(cursorManager, notTilted);
            tiltedField.SetValue(cursorManager, tilted);
            cursorManager.StopOverridingCursor();
        }

        public static void ApplyRemoteCursor(ulong playerId)
        {
            if (RemoteCursor.TryGetValue(playerId, out var cursor) && PlayersCharacter.TryGetValue(playerId, out var charName))
            {
                (Image notTilted, Image tilted) = GetCharCookies(charName);
                if (notTilted == null || tilted == null)
                {
                    GD.PrintErr($"[CookieCursor] Failed to apply remote cursor. Assets for '{charName}' missing.");
                    return;
                }

                ImageTexture notTiltedTex = ImageTexture.CreateFromImage(notTilted);
                ImageTexture tiltedTex = ImageTexture.CreateFromImage(tilted);

                var notTiltedTexField = AccessTools.Field(typeof(NRemoteMouseCursor), "_defaultCursorTexture");
                var tiltedTexField = AccessTools.Field(typeof(NRemoteMouseCursor), "_tiltedCursorTexture");
                notTiltedTexField.SetValue(cursor, notTiltedTex);
                tiltedTexField.SetValue(cursor, tiltedTex);

                cursor.RefreshSize();
            }         
        }

        public static void RevertLocalCursor(NCursorManager cursorManager)
        {
            if (defaultCursorNotTilted != null && defaultCursorTilted != null)
            {
                var notTiltedField = AccessTools.Field(typeof(NCursorManager), "_cursorNotTilted");
                var tiltedField = AccessTools.Field(typeof(NCursorManager), "_cursorTilted");
                notTiltedField.SetValue(cursorManager, defaultCursorNotTilted);
                tiltedField.SetValue(cursorManager, defaultCursorTilted);
                cursorManager.StopOverridingCursor();
            }
        }

        public static (Image notTilted, Image tilted ) GetCharCookies(string character)
        {
            if (character == "necrobinder") character = "necro";
            string path = $"res://images/relics/yummy_cookie_{character}.png";
            var texture = PreloadManager.Cache.GetAsset<Texture2D>(path);
            if (texture == null) return (null, null); 

            Image notTilted = texture.GetImage();
            if (notTilted.IsCompressed()) notTilted.Decompress();

            Image temp = (Image)notTilted.Duplicate();
            Image tilted = RotateImageManually(temp, -10f);

            int newWidth = notTilted.GetWidth() / 4;
            int newHeight = notTilted.GetHeight() / 4;
            notTilted.Resize(newWidth, newHeight, Image.Interpolation.Lanczos);
            tilted.Resize(newWidth, newHeight, Image.Interpolation.Lanczos);
            return (notTilted, tilted);
        }

        public static Image RotateImageManually(Image source, float degrees)
        {
            float rad = Mathf.DegToRad(degrees);
            float cos = Mathf.Abs(Mathf.Cos(rad));
            float sin = Mathf.Abs(Mathf.Sin(rad));

            int originalWidth = source.GetWidth();
            int originalHeight = source.GetHeight();

            int boundW = Mathf.CeilToInt(originalWidth * cos + originalHeight * sin);
            int boundH = Mathf.CeilToInt(originalWidth * sin + originalHeight * cos);

            Image rotated = Image.CreateEmpty(boundW, boundH, false, Image.Format.Rgba8);
            rotated.Fill(new Color(0, 0, 0, 0));

            Vector2 sourceCenter = new Vector2(originalWidth / 2f, originalHeight / 2f);
            Vector2 targetCenter = new Vector2(boundW / 2f, boundH / 2f);

            float rCos = Mathf.Cos(rad);
            float rSin = Mathf.Sin(rad);

            for (int y = 0; y < rotated.GetHeight(); y++)
            {
                for (int x = 0; x < rotated.GetWidth(); x++)
                {
                    Vector2 pos = new Vector2(x, y) - targetCenter;

                    float nx = pos.X * rCos + pos.Y * rSin;
                    float ny = -pos.X * rSin + pos.Y * rCos;

                    Vector2 sourcePos = new Vector2(nx, ny) + sourceCenter;

                    if (sourcePos.X >= 0 && sourcePos.X < source.GetWidth() &&
                        sourcePos.Y >= 0 && sourcePos.Y < source.GetHeight())
                    {
                        rotated.SetPixel(x, y, source.GetPixelv((Vector2I)sourcePos));
                    }
                }
            }
            rotated.Resize(originalWidth, originalHeight, Image.Interpolation.Lanczos);
            return rotated;
        }
    }
}