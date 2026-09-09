using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Entities.Players;

namespace CookieCursor
{
    public class Core
    {
        private static readonly System.Reflection.FieldInfo NotTiltedField = AccessTools.Field(typeof(NCursorManager), "_cursorNotTilted");
        private static readonly System.Reflection.FieldInfo TiltedField = AccessTools.Field(typeof(NCursorManager), "_cursorTilted");
        private static readonly System.Reflection.FieldInfo RemoteNotTiltedTexField = AccessTools.Field(typeof(NRemoteMouseCursor), "_defaultCursorTexture");
        private static readonly System.Reflection.FieldInfo RemoteTiltedTexField = AccessTools.Field(typeof(NRemoteMouseCursor), "_tiltedCursorTexture");
        private static readonly string ModDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        private static readonly string CustomCursorsDir = Path.Combine(ModDir, "Cursors");
        private static readonly string ConfigPath = Path.Combine(ModDir, "Cursor.cfg");

        private static readonly Dictionary<string, (string Path, float TiltAngle, Vector2 BaseHotSpot)> CookieConfigs = new Dictionary<string, (string Path, float TiltAngle, Vector2 BaseHotSpot)>
        {
            { "default", ("res://images/packed/common_ui/cursor_default.png", 0f, new Vector2(14f, 5f))},
            { "ironclad", ("res://images/relics/yummy_cookie_ironclad.png", 5.0f, new Vector2(14f, 3f))},
            { "silent", ("res://images/relics/yummy_cookie_silent.png", 5.0f, new Vector2(11f, 4.5f))},
            { "defect", ("res://images/relics/yummy_cookie_defect.png", 5.0f, new Vector2(15f, 7.5f))},
            { "regent", ("res://images/relics/yummy_cookie_regent.png", 5.0f, new Vector2(11f, 3f))},
            { "necrobinder", ("res://images/relics/yummy_cookie_necro.png", 5.0f, new Vector2(8f, 3f))}
        };

        private static readonly Dictionary<string, (Image notTilted, Image tilted, Vector2 BaseHotSpot, DateTime LastWriteTime)> RotatedCookieCache = new Dictionary<string, (Image, Image, Vector2 BaseHotSpot, DateTime LastWriteTime)>();
        public static Dictionary<ulong, string> PlayersCharacter { get; set; } = new Dictionary<ulong, string>();
        public static Dictionary<ulong, NRemoteMouseCursor> RemoteCursor { get; set; } = new Dictionary<ulong, NRemoteMouseCursor>();

        public static Image defaultCursorNotTilted = null;
        public static Image defaultCursorTilted = null;
        public static Vector2 defaultHotSpot;

        public static float CursorScale = 1.0f;
        public static float CursorOpacity = 0.5f;
        public static int MaxWidth = 64;
        public static int MaxHeight = 64;

        public static void ApplyLocalCursor(string character, NCursorManager cursorManager)
        {
            if (cursorManager != null)
            {
                if (defaultCursorNotTilted == null)
                {
                    CacheDefaultCursorImage(cursorManager);
                }
                
                (Image notTilted, Image tilted, Vector2 hotSpot) = GetCharCookies(character);
                if (notTilted == null || tilted == null)
                {
                    GD.PrintErr($"[CookieCursor] Failed to apply local cursor. Assets for '{character}' missing.");
                    return;
                }

                ref Vector2 defaultHotSpotRef = ref AccessTools.StaticFieldRefAccess<Vector2>(typeof(NCursorManager), "_defaultHotSpot");
                defaultHotSpotRef = hotSpot;

                Image oldNotTilted = (Image)NotTiltedField.GetValue(cursorManager);
                Image oldTilted = (Image)TiltedField.GetValue(cursorManager);

                NotTiltedField.SetValue(cursorManager, notTilted);
                TiltedField.SetValue(cursorManager, tilted);

                if (oldNotTilted != null && oldNotTilted != defaultCursorNotTilted)
                {
                    oldNotTilted.Dispose();
                }
                if (oldTilted != null && oldTilted != defaultCursorTilted)
                {
                    oldTilted.Dispose();
                }

                cursorManager.StopOverridingCursor();
            }

        }

        public static void ApplyRemoteCursor(ulong playerId)
        {
            if (RemoteCursor.TryGetValue(playerId, out var cursor))
            {
                if (!PlayersCharacter.TryGetValue(playerId, out var charName)) {
                    charName = "default";
                }

                if (!GodotObject.IsInstanceValid(cursor) || cursor.IsQueuedForDeletion())
                {
                    GD.PrintErr($"[CookieCursor] Remote cursor for player {playerId} was disposed. Removing invalid reference.");
                    RemoteCursor.Remove(playerId);
                    return;
                }

                (Image notTilted, Image tilted, Vector2 _) = GetCharCookies(charName);
                if (notTilted == null || tilted == null)
                {
                    GD.PrintErr($"[CookieCursor] Failed to apply remote cursor. Assets for '{charName}' missing.");
                    return;
                }

                ImageTexture notTiltedTex = ImageTexture.CreateFromImage(notTilted);
                ImageTexture tiltedTex = ImageTexture.CreateFromImage(tilted);
                notTilted.Dispose();
                tilted.Dispose();

                if (cursor != null)
                {
                    ImageTexture oldNotTiltedTex = (ImageTexture)RemoteNotTiltedTexField.GetValue(cursor);
                    ImageTexture oldTiltedTex = (ImageTexture)RemoteTiltedTexField.GetValue(cursor);

                    RemoteNotTiltedTexField.SetValue(cursor, notTiltedTex);
                    RemoteTiltedTexField.SetValue(cursor, tiltedTex);
                    cursor.RefreshSize();

                    if (oldNotTiltedTex != null) oldNotTiltedTex.Dispose();
                    if (oldTiltedTex != null) oldTiltedTex.Dispose();
                }
            }
        }

        public static void RevertLocalCursor(NCursorManager cursorManager)
        {
            ApplyLocalCursor("default", cursorManager);
        }

        public static void CacheDefaultCursorImage(NCursorManager cursorManager)
        {
            ref Vector2 defaultHotSpotRef = ref AccessTools.StaticFieldRefAccess<Vector2>(typeof(NCursorManager), "_defaultHotSpot");
            if (defaultCursorNotTilted == null && defaultCursorTilted == null)
            {
                GD.Print($"[CookieCursor] Storing defaultCursor image");
                defaultCursorNotTilted = (Image)NotTiltedField.GetValue(cursorManager)!;
                defaultCursorTilted = (Image)TiltedField.GetValue(cursorManager)!;
            }
            if (defaultHotSpot.Length() == 0)
            {
                defaultHotSpot = defaultHotSpotRef;
                GD.Print($"[CookieCursor] Store defaultHotSpot = {defaultHotSpot}");
            }
        }

        public static void ResetCache()
        {
            PlayersCharacter.Clear();
            RemoteCursor.Clear();
        }

        public static void OnChangeCursorScale(double scale)
        {
            CursorScale = (float)scale / 100;
            NCursorManager cursorManager = NGame.Instance?.CursorManager;   
            if (cursorManager != null) {
                if (PlayersCharacter.Count == 0)
                {
                    ApplyLocalCursor("default", cursorManager);
                }
                else
                {
                    RunManager runManager = RunManager.Instance;
                    ulong localId = runManager.NetService.NetId;
                    foreach (var (playerId, character) in PlayersCharacter)
                    {
                        if (playerId == localId)
                        {
                            ApplyLocalCursor(character, cursorManager);
                        }
                        else
                        {
                            ApplyRemoteCursor(playerId);
                        }
                    }
                }
            }
            SaveConfig();
        }

        public static void OnChangeRemoteCursorOpacity(double opacity)
        {
            CursorOpacity = (float)opacity / 100;
            foreach (var (_, cursor) in RemoteCursor) {
                TextureRect texRect = cursor.GetNodeOrNull<TextureRect>("TextureRect");
                if (texRect != null)
                {
                    texRect.SelfModulate = new Godot.Color(1f, 1f, 1f, CursorOpacity);
                }
            }
            SaveConfig();
        }

        public static void SaveConfig()
        {
            ConfigFile config = new ConfigFile();
            config.SetValue("Settings", "CursorScale", CursorScale);
            config.SetValue("Settings", "CursorOpacity", CursorOpacity);
            Error err = config.Save(ConfigPath);
            if (err != Error.Ok)
            {
                GD.PrintErr("[CookieCursor] Failed to save config to local folder. Error code: " + err);
            }
            else
            {
                GD.Print($"[CookieCursor] Config saved to local folder successfully. CursorScale: {CursorScale}, CursorOpacity: {CursorOpacity}");
            }
        }

        public static void LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                GD.Print("[CookieCursor] Local config file not found. Creating new file with default config.");
                CursorScale = 1.0f;
                CursorOpacity = 0.5f;
                SaveConfig();
                return;
            }

            ConfigFile config = new ConfigFile();
            Error err = config.Load(ConfigPath);

            if (err == Error.Ok)
            {
                CursorScale = (float)config.GetValue("Settings", "CursorScale", 1.0f);
                CursorOpacity = (float)config.GetValue("Settings", "CursorOpacity", 0.5f);
                GD.Print($"[CookieCursor] Config loaded from local folder. CursorScale: {CursorScale}, CursorOpacity: {CursorOpacity}");
            }
            else
            {
                GD.PrintErr("[CookieCursor] Failed to load local config file. Using default config");
                CursorScale = 1.0f;
                CursorOpacity = 0.5f;
            }
        }

        public static void InitDefaultIconDir()
        {
            try
            {
                if (!Directory.Exists(CustomCursorsDir))
                {
                    Directory.CreateDirectory(CustomCursorsDir);
                    GD.Print($"[CookieCursor] Created base Cursors directory: {CustomCursorsDir}");
                }

                foreach (var entry in CookieConfigs)
                {
                    if (entry.Key == "default") continue;  
                    string character = entry.Key;
                    string resPath = entry.Value.Path;
                    string charDir = Path.Combine(CustomCursorsDir, character);
                    if (!Directory.Exists(charDir))
                    {
                        Directory.CreateDirectory(charDir);
                        GD.Print($"[CookieCursor] Created character directory: {charDir}");
                    }
                    string iconPath = Path.Combine(charDir, "icon.png");
                    if (!File.Exists(iconPath))
                    {
                        Texture2D texture = PreloadManager.Cache.GetAsset<Texture2D>(resPath);
                        if (texture == null) continue;
                        Image img = texture.GetImage();
                        if (img == null) continue;
                        if (img.IsCompressed()) img.Decompress();
                        img.SavePng(iconPath);
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CookieCursor] Failed to initialize icon directories: {ex.Message}");
            }
        }

        public static string CharacterKey(Player player)
        {
            return player.Character.GetType().Name.ToLowerInvariant();
        }

        /// <summary>
        /// Make sure this player's character has a cursor icon.
        /// The five base game characters are seeded by InitDefaultIconDir from CookieConfigs.
        /// Characters added by other mods are not in that table, so we ask the game for the
        /// Yummy Cookie art it would show for this character and seed the icon from that.
        /// An icon.png that is already on disk always wins, so hand made icons are never overwritten.
        /// </summary>
        public static void EnsureCharacterIcon(Player player)
        {
            string character = CharacterKey(player);
            if (CookieConfigs.ContainsKey(character)) return;

            try
            {
                string charDir = Path.Combine(CustomCursorsDir, character);
                string iconPath = Path.Combine(charDir, "icon.png");
                if (File.Exists(iconPath)) return;

                Image? cookie = GetCookieImage(player);
                if (cookie == null)
                {
                    GD.Print($"[CookieCursor] No Yummy Cookie art available for '{character}'. Keeping the default cursor.");
                    return;
                }

                if (!Directory.Exists(charDir))
                {
                    Directory.CreateDirectory(charDir);
                    GD.Print($"[CookieCursor] Created character directory: {charDir}");
                }
                cookie.SavePng(iconPath);
                cookie.Dispose();
                GD.Print($"[CookieCursor] Seeded cursor icon for '{character}' from its Yummy Cookie art: {iconPath}");
                CacheCharCookies();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CookieCursor] Failed to seed cursor icon for '{character}': {ex.Message}");
            }
        }

        /// <summary>
        /// Ask the game which Yummy Cookie art this player's character uses.
        /// YummyCookie picks its icon from Owner.Character, so we hand a throwaway mutable copy
        /// the player and read the icon back. Character mods that give the cookie their own art
        /// are picked up automatically; ones that do not fall back to whatever the base game
        /// shows for them, which is what the relic bar shows too.
        /// </summary>
        private static Image? GetCookieImage(Player player)
        {
            RelicModel cookie = ModelDb.Relic<YummyCookie>().ToMutable();
            cookie.Owner = player;
            Texture2D texture = cookie.BigIcon;
            if (texture == null) return null;
            Image image = texture.GetImage();
            if (image == null) return null;
            if (image.IsCompressed()) image.Decompress();
            return image;
        }

        public static void CacheCharCookies()
        {
            string[] subDirectories = Directory.GetDirectories(CustomCursorsDir);
            foreach (string dirPath in subDirectories) {
                string folderName = Path.GetFileName(dirPath);
                string character = folderName.ToLowerInvariant();
                if (character == "default") continue;
                string iconPath = Path.Combine(dirPath, "icon.png");
                if (!File.Exists(iconPath)) continue;
                DateTime fileLastWriteTime = File.GetLastWriteTimeUtc(iconPath);
                if (RotatedCookieCache.TryGetValue(character, out var cachedData))
                {
                    if (cachedData.LastWriteTime == fileLastWriteTime)
                    {
                        continue;
                    }
                }
                Image tempNotTilted = Image.LoadFromFile(iconPath);
                if (tempNotTilted.IsCompressed()) tempNotTilted.Decompress();
                float tiltAngle = 5.0f;
                Vector2 hotspot = new Vector2(14f, 5f);
                if (CookieConfigs.TryGetValue(character, out var config))
                {
                    tiltAngle = config.TiltAngle;
                    hotspot = config.BaseHotSpot;
                }
                Image notTilted = RotateImageManually(tempNotTilted, tiltAngle);
                Image tilted = RotateImageManually(tempNotTilted, -tiltAngle);
                tempNotTilted.Dispose();
                var baseImages = (notTilted, tilted, hotspot, fileLastWriteTime);
                RotatedCookieCache[character] = baseImages;
            }
            GD.Print($"[CookieCursor] Pre-cached {RotatedCookieCache.Count} character cursor sets successfully.");
        }

        // Interal
        private static (Image notTilted, Image tilted, Vector2 hotSpot) GetCharCookies(string character)
        {
            if (!RotatedCookieCache.TryGetValue(character, out var baseImages)) {
                if (character == "default")
                {
                    baseImages = (defaultCursorNotTilted, defaultCursorTilted, defaultHotSpot, DateTime.Now);
                    RotatedCookieCache[character] = baseImages;
                } 
                else
                {
                    GD.Print($"[CookieCursor] Unknown character requested: '{character}'. Creating character directory...");
                    string unknownCharDir = Path.Combine(CustomCursorsDir, character);
                    if (!Directory.Exists(unknownCharDir))
                    {
                        try
                        {
                            Directory.CreateDirectory(unknownCharDir);
                            GD.Print($"[CookieCursor] Auto-created directory for new character: {unknownCharDir}");
                        }
                        catch (Exception ex)
                        {
                            GD.PrintErr($"[CookieCursor] Failed to create directory for '{character}': {ex.Message}");
                        }
                    }
                    return (null, null, Vector2.Zero);
                }
            }

            Image finalNotTilted = (Image)baseImages.notTilted.Duplicate();
            Image finalTilted = (Image)baseImages.tilted.Duplicate();
            Vector2 finalHotSpot = baseImages.BaseHotSpot;

            int newWidth = (int)(MaxWidth * CursorScale);
            int newHeight = (int)(MaxHeight * CursorScale);

            Vector2 hotSpot = new Vector2(finalHotSpot.X * CursorScale, finalHotSpot.Y * CursorScale);
            finalNotTilted.Resize(newWidth, newHeight, Image.Interpolation.Lanczos);
            finalTilted.Resize(newWidth, newHeight, Image.Interpolation.Lanczos);
            return (finalNotTilted, finalTilted, hotSpot);
        }

        private static Image RotateImageManually(Image source, float degrees)
        {
            if (degrees == 0f)
            {
                Image copy = new Image();
                copy.CopyFrom(source);
                return copy;
            }
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
