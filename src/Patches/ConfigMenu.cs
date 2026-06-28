using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using static Godot.Control;
using Control = Godot.Control;

namespace CookieCursor
{
    [HarmonyPatch(typeof(NSettingsPanel), nameof(NSettingsPanel._Ready))]
    public class AddCursorConfigMenu
    {
        static void Prefix(NSettingsPanel __instance)
        {
            if (__instance.Name != "GraphicsSettings") return;

            VBoxContainer content = __instance.GetNodeOrNull<VBoxContainer>("VBoxContainer");
            CreateNewSettingSlider(content, "CursorOpacity", Core.CursorOpacity, Core.OnChangeRemoteCursorOpacity);
            CreateNewSettingSlider(content, "CursorScale", Core.CursorScale, Core.OnChangeCursorScale);
            GD.Print("[CookieCursor] Successfully loaded and injected the slider prefab.");
        }

        private static void CreateNewSettingSlider(VBoxContainer content, string settingName, float initValue, Action<double> onValueChanged)
        {
            if (content == null || content.HasNode(settingName)) return;

            var sliderScene = GD.Load<PackedScene>("res://scenes/screens/settings_slider.tscn");
            if (sliderScene == null)
            {
                GD.PrintErr("[CookieCursor] Failed to load settings_slider.tscn from the packaged files.");
                return;
            }

            // Steal and dupliicate some nodes from "VSync"
            MarginContainer stolenContainer = content.GetNodeOrNull<MarginContainer>("VSync");
            MegaRichTextLabel stolenLabel = stolenContainer.GetNodeOrNull<MegaRichTextLabel>("Label");
            ColorRect stolenDivider = content.GetNodeOrNull<ColorRect>("FullscreenDivider");

            if (sliderScene == null || stolenContainer == null || stolenLabel == null || stolenDivider == null)
            {
                GD.PrintErr("[CookieCursor] Failed to steal node for cloning");
                return;
            }

            // Setup container
            Control clonedContainer = stolenContainer.Duplicate() as Control;
            ClearAllChildrenImmediately(clonedContainer);
            clonedContainer.Name = settingName + "Container";
            clonedContainer.Visible = true;

            // Setup Label
            MegaRichTextLabel clonedLabel = stolenLabel.Duplicate((int)(
                Node.DuplicateFlags.Groups |
                Node.DuplicateFlags.Scripts |
                Node.DuplicateFlags.UseInstantiation
            )) as MegaRichTextLabel;
            clonedLabel.SetTextAutoSize(settingName);
            clonedLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            clonedLabel.VerticalAlignment = VerticalAlignment.Center;

            // Setup Slider
            Control sliderChunk = sliderScene.Instantiate() as Control;
            if (sliderChunk == null)
            {
                GD.PrintErr("[CookieCursor] Failed to create slider");
                return;
            }
            SetupSlider(sliderChunk, settingName, initValue, onValueChanged);
            
            // Compose the cloned container
            clonedContainer.AddChild(clonedLabel);
            clonedContainer.AddChild(sliderChunk);

            // Append to top of SettingPanel
            ColorRect divider = stolenDivider.Duplicate() as ColorRect;
            content.AddChild(clonedContainer);
            content.AddChild(divider);
            content.MoveChild(clonedContainer, 0);
            content.MoveChild(divider, 1);
        }

        private static void SetupSlider(Control sliderChunk, string name, float initValue, Action<double> onValueChanged)
        {
            sliderChunk.Name = name;
            sliderChunk.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            sliderChunk.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            MegaLabel valueLabel = sliderChunk.GetNode<MegaLabel>("SliderValue");
            NSlider internalSlider = sliderChunk.GetNode<NSlider>("Slider");
            NSelectionReticle interalReticle = sliderChunk.GetNode<NSelectionReticle>("SelectionReticle");
            if (internalSlider == null)
            {
                GD.PrintErr("[CookieCursor] Failed to find NSlider");
                return;
            }

            internalSlider.MinValue = 5;
            internalSlider.MaxValue = 100;
            internalSlider.Step = 5;
            internalSlider.Value = (double)(initValue * 100);

            if (valueLabel != null) valueLabel.SetTextAutoSize($"{internalSlider.Value}%");

            internalSlider.Connect(
                NSlider.SignalName.ValueChanged,
                Callable.From<double>((val) => {
                    if (valueLabel != null) valueLabel.SetTextAutoSize($"{internalSlider.Value}%");
                })
            );

            internalSlider.Connect(
                NSlider.SignalName.MouseReleased,
                Callable.From<double>((_) => {
                    onValueChanged(internalSlider.Value);
                })
            );

            sliderChunk.Connect(
                Control.SignalName.FocusEntered,
                Callable.From(() =>
                {
                    if (NControllerManager.Instance.IsUsingController)
                    {
                        interalReticle.OnSelect();
                    }
                })
            );

            sliderChunk.Connect(
                Control.SignalName.FocusExited,
                Callable.From(() =>
                {
                    interalReticle.OnDeselect();
                })
            );

            sliderChunk.Connect(
                Control.SignalName.GuiInput,
                Callable.From<InputEvent>((input) => { 
                    if (sliderChunk.HasFocus())
                    {
                        if (input.IsActionPressed(MegaInput.left))
                        {
                            internalSlider.Value -= 5.0;
                            onValueChanged(internalSlider.Value);
                            sliderChunk.AcceptEvent();
                        }

                        if (input.IsActionPressed(MegaInput.right))
                        {
                            internalSlider.Value += 5.0;
                            onValueChanged(internalSlider.Value);
                            sliderChunk.AcceptEvent();
                        }
                    }
                })
            );
        }

        private static void ClearAllChildrenImmediately(Node parent)
        {
            var children = parent.GetChildren();
            for (int i = children.Count - 1; i >= 0; i--)
            {
                children[i].Free();
            }
        }

        private static void DumpNodeTreeRecursive(Node node, string indent)
        {

            GD.Print($"{indent}- [{node.GetType().Name}] \"{node.Name}\"");

            foreach (Node child in node.GetChildren())
            {
                DumpNodeTreeRecursive(child, indent + "  ");
            }
        }
    }

    [HarmonyPatch(typeof(NSettingsPanel), "IsSettingsOption")]
    public static class PatchIsSettingsOption
    {
        static void Postfix(Control c, ref bool __result)
        {
            if (__result) return;

            if (c.Name == "CursorOpacity" || c.Name == "CursorScale")
            {
                if (c.FocusMode != FocusModeEnum.All)
                {
                    c.FocusMode = FocusModeEnum.All;
                }
                __result = true;
            }
        }
    }
}
