using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;

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
                        Core.PlayersCharacter[player.NetId] = character;

                        if (runManager.NetService.NetId == player.NetId)
                        {
                            Core.ApplyLocalCursor(Core.PlayersCharacter[player.NetId], cursorManager);
                        }
                        else
                        {
                            Core.ApplyRemoteCursor(player.NetId);
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
    public static class StoreOriginCursorImage
    {
        static void Postfix()
        {
            var cursorManager = NGame.Instance?.CursorManager;
            Core.LoadConfig();
            Core.StoreDefaultCursorImage(cursorManager);
            Core.ApplyLocalCursor("default", cursorManager);
        }
    }

    [HarmonyPatch(typeof(NRemoteMouseCursor), nameof(NRemoteMouseCursor.Create))]
    public static class StoreRemoteCursor
    {
        static void Postfix(ulong playerId, NRemoteMouseCursor __result)
        {
            Core.RemoteCursor[playerId] = __result;
            TextureRect texRect = __result.GetNodeOrNull<TextureRect>("TextureRect");
            if (texRect != null)
            {
                texRect.SelfModulate = new Godot.Color(1f, 1f, 1f, Core.CursorOpacity);
            }
            Callable.From(() =>
            {
                Core.ApplyRemoteCursor(playerId);
                GD.Print($"[CookieCursor] Applied remote cursor for player {playerId} deferred.");
            }).CallDeferred();
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
                Core.RevertLocalCursor(cursorManager);
            }
            Core.PlayersCharacter.Clear();
            Core.RemoteCursor.Clear();
        }
    }

}
