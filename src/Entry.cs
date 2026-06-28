using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace CookieCursor
{
    [ModInitializer("Initialize")]
    public class Entry
    {
        public static void Initialize()
        {
            var harmony = new Harmony("com.sincel.cookiecursor");
            harmony.PatchAll();
            GD.Print("[CookieCursor] Mod initialized successfully.");
        }
    }
}