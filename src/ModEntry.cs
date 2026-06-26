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
    public static class ReplaceCursor
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
                    foreach (Player player in state.Players)
                    {
                        string character = player.Character.GetType().Name.ToLower();
                        ModEntry.PlayersCharacter[player.NetId] = character;

                        if (runManager.NetService.NetId == player.NetId) 
                        {
                            ModEntry.LocalId = player.NetId;
                            ModEntry.ApplyLocalCursor(ModEntry.PlayersCharacter[player.NetId], cursorManager);
                        }
                        else
                        {
                            ModEntry.ApplyRemoteCursor(player.NetId);
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(NRemoteMouseCursor), nameof(NRemoteMouseCursor.Create))]
    public static class StoreRemoteCursor
    {
        static void Postfix(ulong playerId, NRemoteMouseCursor __result)
        {
            ModEntry.RemoteCursor[playerId] = __result;

            if (ModEntry.PlayersCharacter.ContainsKey(playerId))
            {
                Callable.From(() =>
                {
                    ModEntry.ApplyRemoteCursor(playerId);
                    GD.Print($"[CookieCursor] Applied remote cursor for player {playerId} deferred.");
                }).CallDeferred();
            }
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
        private static readonly System.Reflection.FieldInfo NotTiltedField = AccessTools.Field(typeof(NCursorManager), "_cursorNotTilted");
        private static readonly System.Reflection.FieldInfo TiltedField = AccessTools.Field(typeof(NCursorManager), "_cursorTilted");
        private static readonly System.Reflection.FieldInfo RemoteNotTiltedTexField = AccessTools.Field(typeof(NRemoteMouseCursor), "_defaultCursorTexture");
        private static readonly System.Reflection.FieldInfo RemoteTiltedTexField = AccessTools.Field(typeof(NRemoteMouseCursor), "_tiltedCursorTexture");
        
        private static readonly Dictionary<string, (string Path, float TiltAngle)> CookieConfigs = new Dictionary<string, (string Path, float TiltAngle)>
        {
            { "ironclad", ("res://images/relics/yummy_cookie_ironclad.png", 3.0f) },
            { "silent", ("res://images/relics/yummy_cookie_silent.png", 6.5f) },
            { "defect", ("res://images/relics/yummy_cookie_defect.png", 6.5f) },
            { "regent", ("res://images/relics/yummy_cookie_regent.png", 4.0f) },
            { "necrobinder", ("res://images/relics/yummy_cookie_necro.png", 10.0f) }
        };

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

            if (cursorManager != null)
            {
                if (defaultCursorNotTilted == null && defaultCursorTilted == null)
                {
                    defaultCursorNotTilted = (Image)NotTiltedField.GetValue(cursorManager)!;
                    defaultCursorTilted = (Image)TiltedField.GetValue(cursorManager)!;
                }
                NotTiltedField.SetValue(cursorManager, notTilted);
                TiltedField.SetValue(cursorManager, tilted);
                cursorManager.StopOverridingCursor();
            }
            
        }

        public static void ApplyRemoteCursor(ulong playerId)
        {
            if (RemoteCursor.TryGetValue(playerId, out var cursor) && PlayersCharacter.TryGetValue(playerId, out var charName))
            {
                if (!GodotObject.IsInstanceValid(cursor) || cursor.IsQueuedForDeletion())
                {
                    GD.PrintErr($"[CookieCursor] Remote cursor for player {playerId} was disposed. Removing invalid reference.");
                    RemoteCursor.Remove(playerId);
                    return;
                }

                (Image notTilted, Image tilted) = GetCharCookies(charName);
                if (notTilted == null || tilted == null)
                {
                    GD.PrintErr($"[CookieCursor] Failed to apply remote cursor. Assets for '{charName}' missing.");
                    return;
                }

                ImageTexture notTiltedTex = ImageTexture.CreateFromImage(notTilted);
                ImageTexture tiltedTex = ImageTexture.CreateFromImage(tilted);
                
                if (cursor != null)
                {
                    RemoteNotTiltedTexField.SetValue(cursor, notTiltedTex);
                    RemoteTiltedTexField.SetValue(cursor, tiltedTex);
                    cursor.RefreshSize();
                }
            }
        }

        public static void RevertLocalCursor(NCursorManager cursorManager)
        {
            if (defaultCursorNotTilted != null && defaultCursorTilted != null)
            {
                NotTiltedField.SetValue(cursorManager, defaultCursorNotTilted);
                TiltedField.SetValue(cursorManager, defaultCursorTilted);
                cursorManager.StopOverridingCursor();
            }
            defaultCursorNotTilted = null;
            defaultCursorTilted = null;
        }

        public static (Image notTilted, Image tilted) GetCharCookies(string character)
        {
            if (!CookieConfigs.TryGetValue(character, out var config))
            {
                GD.PrintErr("[CookieCursor] Unknown character requested: " + character);
                return (null, null);
            }

            var texture = PreloadManager.Cache.GetAsset<Texture2D>(config.Path);
            if (texture == null)
            {
                GD.PrintErr("[CookieCursor] Failed to load texture at path: " + config.Path);
                return (null, null);
            }

            Image notTilted = texture.GetImage();
            if (notTilted.IsCompressed()) notTilted.Decompress();

            Image temp = (Image)notTilted.Duplicate();
            Image tilted = RotateImageManually(temp, -config.TiltAngle);
            notTilted = RotateImageManually(notTilted, config.TiltAngle);
            temp.Dispose();

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