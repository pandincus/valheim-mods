using System.Collections.Generic;
using BubbleBar.Logic;
using TMPro;
using UnityEngine;

namespace BubbleBar
{
    /// <summary>
    /// Draws the barrier: one bar on your own HUD, and one over each ally who has a barrier.
    /// </summary>
    /// <remarks>
    /// Everything here is driven from <see cref="BubbleBarPlugin.Update"/> rather than from a
    /// patch on <c>Hud</c> or <c>EnemyHud</c>. Both of those would work, and both would be one
    /// more method Iron Gate can move out from under us - ChattyBones lost three hooks to a
    /// single patch release. A sweep reading <c>Hud.instance</c> and <c>EnemyHud.instance</c>
    /// needs no patch at all, so this mod still has exactly one.
    ///
    /// Both bars are clones of a bar the game already draws, which is why they look like they
    /// belong. The ally bar is parented to the hud it belongs to, so when EnemyHud destroys
    /// that hud our bar goes with it and there is nothing to clean up.
    /// </remarks>
    internal static class Bars
    {
        /// <summary>What we name our clones, and how we find them again.</summary>
        private const string BarName = "BubbleBar";

        /// <summary>
        /// What we rename the one fill we keep, and how we find it again.
        /// </summary>
        /// <remarks>
        /// Renaming rather than looking the fill up by component each frame. A health group
        /// holds three <c>GuiBar</c>s and <c>GetComponentInChildren</c> answers with whichever
        /// comes first in the hierarchy, which is not the one we picked - so the value would go
        /// to a bar we had just switched off, while the visible one sat frozen at whatever
        /// length it happened to be cloned at.
        /// </remarks>
        private const string FillName = "BubbleBarFill";

        private static GameObject _ownBar;
        private static RectTransform _ownRect;
        private static readonly List<GuiBar> _ownFills = [];

        /// <summary>The health bar's own number, reused to show absorb remaining.</summary>
        private static TMP_Text _ownNumber;

        /// <summary>
        /// What <c>OwnBarThickness = 1</c> actually scales the cloned bar to.
        /// </summary>
        /// <remarks>
        /// The health bar is noticeably chunkier than the stamina and eitr bars it now sits
        /// under, so a raw clone looks out of place beside them. 0.6 is the value that matched
        /// them in game - found by dragging the slider and looking, which is the only way this
        /// number was ever going to be found. The setting stays a multiplier on top, so 1 means
        /// "what the mod ships with" rather than "whatever the health bar happens to be".
        /// </remarks>
        private const float ShippedThickness = 0.6f;

        /// <summary>Set when building the own bar failed, so we stop trying.</summary>
        private static bool _ownBarFailed;

        /// <summary>Whether anything is currently on screen, so we only sweep them away once.</summary>
        private static bool _showing;

        private static string _colorText;
        private static Color _color = Color.cyan;

        /// <summary>Forget the HUD we built against. Called when the world unloads.</summary>
        /// <remarks>
        /// Unity has already destroyed the objects by then; this just drops our references so
        /// the next world builds fresh ones rather than writing into a destroyed transform.
        /// </remarks>
        internal static void Reset()
        {
            _ownBar = null;
            _ownRect = null;
            _ownNumber = null;
            _ownFills.Clear();
            _ownBarFailed = false;
            _showing = false;
        }

        /// <summary>Update both bars. Called once a frame.</summary>
        internal static void Tick()
        {
            if (!ModConfig.Enabled.Value)
            {
                Hide();
                return;
            }

            Player me = Player.m_localPlayer;

            if (me == null)
            {
                Hide();
                return;
            }

            RefreshColor();
            TickOwnBar(me);
            TickAllyBars(me);
            _showing = true;
        }

        /// <summary>Re-parse the configured color, but only when the text has changed.</summary>
        private static void RefreshColor()
        {
            string wanted = ModConfig.BarColor.Value;

            if (wanted == _colorText)
            {
                return;
            }

            _colorText = wanted;

            if (!ColorUtility.TryParseHtmlString(wanted, out Color parsed))
            {
                BubbleBarPlugin.Log.LogWarning(
                    "BarColor is set to \"" + wanted + "\", which is not a color I can read. "
                    + "Use something like " + ModConfig.DefaultBarColor + ". Falling back to the default.");

                _ = ColorUtility.TryParseHtmlString(ModConfig.DefaultBarColor, out parsed);
            }

            _color = parsed;
        }

        /// <summary>Put every bar away, on both surfaces.</summary>
        /// <remarks>
        /// The ally bars have to be swept explicitly. They are children of somebody else's hud
        /// and nothing else will touch them, so flipping <c>Enabled</c> off mid-game without
        /// this leaves them frozen on screen at whatever they last read - which is exactly what
        /// a kill switch must not do.
        ///
        /// <see cref="_showing"/> is only cleared once the sweep has actually finished, so an
        /// EnemyHud that happens to be missing this frame gets swept on the next one rather
        /// than leaving its bars up for good.
        /// </remarks>
        internal static void Hide()
        {
            if (!_showing)
            {
                return;
            }

            if (_ownBar != null)
            {
                _ownBar.SetActive(false);
            }

            EnemyHud hud = EnemyHud.instance;

            if (hud == null)
            {
                return;
            }

            foreach (KeyValuePair<Character, EnemyHud.HudData> entry in hud.m_huds)
            {
                if (entry.Value == null || entry.Value.m_gui == null)
                {
                    continue;
                }

                Transform bar = entry.Value.m_gui.transform.Find(BarName);

                if (bar != null)
                {
                    bar.gameObject.SetActive(false);
                }
            }

            _showing = false;
        }

        /// <summary>Draw the local player's own barrier on the main HUD.</summary>
        /// <param name="me">The local player.</param>
        /// <remarks>
        /// The clone sits beside the eitr bar and takes its position from it every frame plus a
        /// configured offset, which is what keeps it in the right place when the build or ship
        /// HUD slides the whole group up - <c>Hud.UpdateEitr</c> moves the eitr root between
        /// two anchored positions and we would otherwise have to know about both.
        /// </remarks>
        private static void TickOwnBar(Player me)
        {
            Hud hud = Hud.instance;

            if (hud == null || hud.m_eitrBarRoot == null || hud.m_healthBarRoot == null)
            {
                return;
            }

            if (_ownBar == null && !BuildOwnBar(hud))
            {
                return;
            }

            ShieldReading reading = ModConfig.ShowOwnBar.Value ? Shields.Read(me) : ShieldReading.None;

            _ownBar.SetActive(reading.IsPresent);

            if (!reading.IsPresent)
            {
                return;
            }

            _ownRect.anchoredPosition = hud.m_eitrBarRoot.anchoredPosition
                + new Vector2(ModConfig.OwnBarOffsetX.Value, ModConfig.OwnBarOffsetY.Value);

            float width = ModConfig.OwnBarWidth.Value;

            // The root and its fills get the same number here, which is what
            // Hud.SetHealthBarSize does - the eitr bar is the one that adds a border buffer to
            // its root, and this is no longer cloned from that.
            _ownRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

            // Thickness is a scale rather than a height, because height is the one dimension
            // nothing here controls: vanilla only ever sizes the health bar along X, and the
            // fills inside are anchored to the root in ways I cannot see from the decompile.
            // Scaling the whole group is the version that cannot half-work.
            float thickness = ShippedThickness * ModConfig.OwnBarThickness.Value;

            _ownRect.localScale = new Vector3(1f, thickness, 1f);

            for (int i = 0; i < _ownFills.Count; i++)
            {
                _ownFills[i].SetWidth(width);
                _ownFills[i].SetColor(_color);
                _ownFills[i].SetValue(reading.Fraction);
            }

            ShowNumber(me, reading, thickness);
        }

        /// <summary>Write how much damage the barrier can still soak beside the bar.</summary>
        /// <param name="me">The local player.</param>
        /// <param name="reading">What the bar is drawing.</param>
        /// <param name="thickness">The scale the bar is drawn at.</param>
        /// <remarks>
        /// The number is read from <c>SE_Shield</c> rather than from the reading, because a
        /// fraction cannot say 147. This is always the local player, so the barrier is always
        /// ours to look at directly.
        ///
        /// It is counter-scaled because thickness is applied to the whole group, digits
        /// included - at the shipped 0.6 the text would otherwise be squashed to about
        /// three-fifths its height while the font stayed the size it thinks it is.
        ///
        /// Ceiling rather than rounding, to match <c>Hud.UpdateHealth</c>: a barrier with
        /// anything left at all should not read 0.
        /// </remarks>
        private static void ShowNumber(Player me, ShieldReading reading, float thickness)
        {
            if (_ownNumber == null)
            {
                return;
            }

            bool wanted = ModConfig.ShowOwnBarNumber.Value;

            _ownNumber.enabled = wanted;

            if (!wanted || thickness <= 0f)
            {
                return;
            }

            _ownNumber.rectTransform.localScale = new Vector3(1f, 1f / thickness, 1f);

            float capacity = Shields.TryGetShield(me, out SE_Shield shield)
                ? shield.m_totalAbsorbDamage
                : 0f;

            _ownNumber.text = Mathf.CeilToInt(reading.Fraction * capacity).ToString();
        }

        /// <summary>Clone the health bar into a barrier bar.</summary>
        /// <returns>True when the clone is ready to draw.</returns>
        /// <param name="hud">The live HUD.</param>
        /// <remarks>
        /// The health bar rather than the eitr bar, which is the obvious choice and is wrong.
        /// <c>Hud.UpdateEitr</c> drives <c>m_eitrAnimator.SetBool("Visible", ...)</c>, and that
        /// bool is false whenever eitr is at maximum - which includes every character that has
        /// no eitr at all. Cloning copies whatever pose the animator has the bar in and
        /// disabling the clone's animator freezes it there, so a bar cloned from the eitr bar
        /// would usually be invisible with nothing in the log to say why. The health bar's
        /// animator only ever gets <c>SetTrigger("Flash")</c>; it has no hidden state.
        ///
        /// The two are anchored differently, so the clone takes the eitr bar's anchors and
        /// pivot before anything positions it - otherwise an anchoredPosition copied from one
        /// would mean something else entirely on the other.
        /// </remarks>
        private static bool BuildOwnBar(Hud hud)
        {
            if (_ownBarFailed)
            {
                return false;
            }

            // worldPositionStays: false, so the clone keeps its position relative to the HUD
            // canvas rather than its position in the world. Passing it explicitly rather than
            // leaning on the two-argument overload's default, because for a UI element the
            // wrong one is the difference between a bar on screen and a bar nowhere at all.
            GameObject clone = Object.Instantiate(
                hud.m_healthBarRoot.gameObject, hud.m_eitrBarRoot.parent, worldPositionStays: false);

            clone.name = BarName;
            _ownRect = clone.transform as RectTransform;

            GuiBar[] bars = clone.GetComponentsInChildren<GuiBar>(includeInactive: true);

            if (_ownRect == null || bars.Length == 0)
            {
                Object.Destroy(clone);
                _ownRect = null;
                _ownBarFailed = true;

                BubbleBarPlugin.Log.LogWarning(
                    "The health bar is not shaped the way I expected, so your own barrier will "
                    + "have no bar for the rest of this session. The ally bars are unaffected.");

                return false;
            }

            _ownRect.anchorMin = hud.m_eitrBarRoot.anchorMin;
            _ownRect.anchorMax = hud.m_eitrBarRoot.anchorMax;
            _ownRect.pivot = hud.m_eitrBarRoot.pivot;

            // The player's health bar is drawn vertically, and it gets there by being rotated
            // rather than by being built that way - Hud.SetHealthBarSize sizes it along the
            // *horizontal* axis, exactly like the eitr bar. A clone keeps that rotation, so
            // without this the barrier bar stands on end and lies across the stamina and eitr
            // bars, which is precisely what the first playtest showed.
            Vector3 borrowed = _ownRect.localEulerAngles;

            _ownRect.localRotation = Quaternion.identity;
            _ownRect.localScale = Vector3.one;

            Strip(clone);
            AdoptNumber(clone, hud);

            _ownFills.Clear();

            for (int i = 0; i < bars.Length; i++)
            {
                bars[i].gameObject.SetActive(true);
                bars[i].SetMaxValue(1f);
                _ownFills.Add(bars[i]);
            }

            _ownBar = clone;
            _ownBar.SetActive(false);

            // The rotation is logged rather than just cleared, because if the bar ever comes out
            // the wrong way round again this line says whether rotation was the cause.
            BubbleBarPlugin.Log.LogInfo(
                "Built the barrier bar from the health bar (" + _ownFills.Count + " fills, "
                + "cleared a rotation of " + borrowed + "). "
                + "Nudge it with OwnBarOffsetX / OwnBarOffsetY if it sits badly.");

            return true;
        }

        /// <summary>Draw a barrier bar over every ally who has one.</summary>
        /// <param name="me">The local player, for deciding who counts as an ally.</param>
        /// <remarks>
        /// Huds the game is not currently drawing are skipped entirely, and that is
        /// load-bearing rather than an optimisation. <c>GuiBar.Awake</c> captures its full
        /// width from the rect it is given, and Awake does not run until the object is active -
        /// so a bar built into a hidden hud would capture that width later, after we had
        /// already shrunk it to a fraction, and would then treat the fraction as its new full
        /// length.
        /// </remarks>
        private static void TickAllyBars(Player me)
        {
            EnemyHud hud = EnemyHud.instance;

            if (hud == null)
            {
                return;
            }

            foreach (KeyValuePair<Character, EnemyHud.HudData> entry in hud.m_huds)
            {
                Character character = entry.Key;
                EnemyHud.HudData data = entry.Value;

                if (character == null || data == null || data.m_gui == null)
                {
                    continue;
                }

                // Your own barrier is on the main HUD, and EnemyHud would be drawing this over
                // your own head.
                if (ReferenceEquals(character, me) || !data.m_gui.activeInHierarchy)
                {
                    continue;
                }

                bool wanted = ModConfig.ShowAllyBars.Value && Shields.IsAlly(me, character);

                ShieldReading reading = wanted ? Shields.Read(character) : ShieldReading.None;

                Transform found = data.m_gui.transform.Find(BarName);
                GameObject bar = found == null ? null : found.gameObject;

                if (bar == null)
                {
                    // Nothing is built until somebody actually has a barrier, so a deer
                    // wandering past never gets one.
                    if (!reading.IsPresent)
                    {
                        continue;
                    }

                    bar = BuildAllyBar(data);

                    if (bar == null)
                    {
                        continue;
                    }
                }

                bar.SetActive(reading.IsPresent);

                if (!reading.IsPresent)
                {
                    continue;
                }

                // Positioned every frame rather than once at build, so AllyBarOffsetY takes
                // effect the moment it is edited, like every other setting.
                if (data.m_gui.transform.Find("Health") is RectTransform source
                    && bar.transform is RectTransform rect)
                {
                    rect.anchoredPosition = source.anchoredPosition
                        + new Vector2(0f, ModConfig.AllyBarOffsetY.Value);
                }

                Transform fill = bar.transform.Find(FillName);
                GuiBar guiBar = fill == null ? null : fill.GetComponent<GuiBar>();

                if (guiBar == null)
                {
                    continue;
                }

                // The cloned fill captured its width from a health bar that was already being
                // drawn at the ally's health fraction, so a full barrier would otherwise be
                // exactly as long as their remaining health. The hud's own bar knows the real
                // full width.
                if (data.m_healthFast != null)
                {
                    guiBar.SetWidth(data.m_healthFast.m_width);
                }

                guiBar.SetColor(_color);
                guiBar.SetValue(reading.Fraction);
            }
        }

        /// <summary>Clone a hud's health group into a barrier bar for that character.</summary>
        /// <returns>The bar, or null when there is nothing to build from.</returns>
        /// <param name="data">The hud EnemyHud made for this character.</param>
        /// <remarks>
        /// The kept fill is explicitly activated. For an ally it arrives inactive, because
        /// <c>EnemyHud.UpdateHuds</c> switches <c>health_fast</c> off and
        /// <c>health_fast_friendly</c> on for anything friendly - so without this the bar is a
        /// dark empty slot that never fills, which is exactly what it did until a review caught
        /// it.
        /// </remarks>
        private static GameObject BuildAllyBar(EnemyHud.HudData data)
        {
            Transform health = data.m_gui.transform.Find("Health");

            if (health == null)
            {
                return null;
            }

            GameObject clone = Object.Instantiate(
                health.gameObject, data.m_gui.transform, worldPositionStays: false);

            clone.name = BarName;

            GuiBar[] bars = clone.GetComponentsInChildren<GuiBar>(includeInactive: true);
            GuiBar keep = null;

            for (int i = 0; i < bars.Length; i++)
            {
                if (bars[i].name.Contains("fast") && !bars[i].name.Contains("friendly"))
                {
                    keep = bars[i];
                    break;
                }
            }

            if (keep == null && bars.Length > 0)
            {
                keep = bars[0];
            }

            if (keep == null)
            {
                Object.Destroy(clone);
                return null;
            }

            // The health group carries three bars - the fast one, the slow one that trails it,
            // and the friendly-colored variant - plus a number on mounts. We want one, so the
            // rest are switched off rather than deleted, which keeps the group's layout exactly
            // as it was.
            for (int i = 0; i < bars.Length; i++)
            {
                if (!ReferenceEquals(bars[i], keep))
                {
                    bars[i].gameObject.SetActive(false);
                }
            }

            keep.gameObject.SetActive(true);
            keep.name = FillName;
            keep.SetMaxValue(1f);

            Strip(clone);

            return clone;
        }

        /// <summary>Keep the cloned health number, and lay it out the way the eitr number is.</summary>
        /// <param name="clone">The cloned health bar.</param>
        /// <param name="hud">The live HUD, whose eitr number is the model.</param>
        /// <remarks>
        /// Reusing the health bar's own number rather than making one, so it arrives with the
        /// game's font and outline already right - the same reason the bar itself is a clone.
        /// What it does not arrive with is the right position or size: it was laid out beside
        /// a bar standing on end, a little larger than the stamina and eitr numbers, and it
        /// carries its own counter-rotation so the digits read upright against a rotated
        /// parent.
        ///
        /// So the layout is borrowed from the eitr number, which is the bar ours sits under.
        /// When that number hangs directly off the eitr bar its anchors mean the same thing on
        /// ours and are copied exactly; if it is nested somewhere else they would not, and the
        /// number is centred on the bar instead, which is where the stamina and eitr numbers
        /// sit in play.
        ///
        /// <see cref="Strip"/> has already switched it off by then, which is why this runs
        /// after and turns it back on.
        /// </remarks>
        private static void AdoptNumber(GameObject clone, Hud hud)
        {
            _ownNumber = clone.GetComponentInChildren<TMP_Text>(includeInactive: true);

            if (_ownNumber == null)
            {
                BubbleBarPlugin.Log.LogInfo(
                    "The health bar had no number to borrow, so the barrier bar will not show "
                    + "one. Everything else is unaffected.");

                return;
            }

            _ownNumber.gameObject.SetActive(true);
            _ownNumber.enabled = true;

            RectTransform rect = _ownNumber.rectTransform;
            rect.localRotation = Quaternion.identity;

            TMP_Text model = hud.m_eitrText;

            if (model != null && ReferenceEquals(model.rectTransform.parent, hud.m_eitrBarRoot))
            {
                RectTransform source = model.rectTransform;

                rect.anchorMin = source.anchorMin;
                rect.anchorMax = source.anchorMax;
                rect.pivot = source.pivot;
                rect.anchoredPosition = source.anchoredPosition;
                rect.sizeDelta = source.sizeDelta;
            }
            else
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
            }

            if (model != null)
            {
                _ownNumber.fontSize = model.fontSize;
                _ownNumber.alignment = model.alignment;
            }
        }

        /// <summary>Switch off the parts of a cloned bar we do not want.</summary>
        /// <param name="clone">The cloned group.</param>
        /// <remarks>
        /// The animator and any text are picked out by type *name* rather than by referencing
        /// UnityEngine.AnimationModule and Unity.TextMeshPro, because the only thing this mod
        /// would use either assembly for is setting two booleans. Disabling every Behaviour
        /// instead is not an option - the bar art is an Image, which is a Behaviour too, and
        /// the bar would vanish.
        /// </remarks>
        private static void Strip(GameObject clone)
        {
            Behaviour[] parts = clone.GetComponentsInChildren<Behaviour>(includeInactive: true);

            for (int i = 0; i < parts.Length; i++)
            {
                string kind = parts[i].GetType().Name;

                if (kind == "Animator" || kind.Contains("Text"))
                {
                    parts[i].enabled = false;
                }
            }
        }
    }
}
