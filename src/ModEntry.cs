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

    [HarmonyPatch(typeof(NCursorManager), nameof(NCursorManager.StopOverridingCursor))]
    public static class ReplaceCursor
    {
        static void Postfix()
        {
            var cursorManager = NGame.Instance?.CursorManager;
            var runManager = RunManager.Instance;
            if (cursorManager != null && runManager.IsInProgress)
            {
                ModEntry.ApplyLocalCursor(ModEntry.PlayersCharacter[ModEntry.LocalId], cursorManager);
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
                cursorManager.StopOverridingCursor();
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
        public static void Initialize()
        {
            var harmony = new Harmony("com.sincel.cookiecursor");
            harmony.PatchAll();
        }
        public static void ApplyLocalCursor(string character, NCursorManager cursorManager)
        {
            Image image = GetCharCookies(character);
            cursorManager.OverrideCursor(image, image, Vector2.Zero);
        }

        public static void ApplyRemoteCursor(ulong playerId)
        {
            if (RemoteCursor.TryGetValue(playerId, out var cursor) && PlayersCharacter.TryGetValue(playerId, out var charName))
            {
                Image image = GetCharCookies(charName);
                ImageTexture imageTex = ImageTexture.CreateFromImage(image);
                var defaultTex = AccessTools.Field(typeof(NRemoteMouseCursor), "_defaultCursorTexture");
                var tiltedTex = AccessTools.Field(typeof(NRemoteMouseCursor), "_tiltedCursorTexture");
                defaultTex.SetValue(cursor, imageTex);
                tiltedTex.SetValue(cursor, imageTex);
                cursor.RefreshSize();
            }         
        }

        public static Image GetCharCookies(string character)
        {
            if (character == "necrobinder") character = "necro";
            string path = $"res://images/relics/yummy_cookie_{character}.png";
            var texture = PreloadManager.Cache.GetAsset<Texture2D>(path);
            if (texture == null) return null;

            Image image = texture.GetImage();
            if (image.IsCompressed()) image.Decompress();
            int newWidth = image.GetWidth() / 4;
            int newHeight = image.GetHeight() / 4;
            image.Resize(newWidth, newHeight, Image.Interpolation.Lanczos);
            return image;
        }
    }
}