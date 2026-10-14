using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI
{
    // 本機玩家的狀態圖示列:被標記(被敵方 Point Sensor 等看穿)、減速(毒霧)、強化(能量站)。
    // 狀態來源:Marking(被敵方標記)+ InklingModifiers.Status(效果 spec 自帶的旗標)+ Push(外部臨時推送)。
    // 圖示只在狀態存在時出現(彈出動畫),由左往右依序排;被標記時外圈倒數剩餘時間並閃爍,文字「被標記!」。
    // 純 uGUI、建立一次;每幀只改顏色/fill/位置,零配置。MatchHud 與 StatusHud(非對戰場景)共用。
    public sealed class StatusIconRow
    {
        private sealed class Slot
        {
            public StatusFlags Flag;
            public RectTransform Root;
            public Image Back;
            public Image Timer;
            public Image Icon;
            public Text Label;
            public bool Shown;
            public float ShownAt;
            public float Total;
        }

        private const float SlotSize = 64f;
        private const float Spacing = 84f;

        private static readonly Color BackColor = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color SlowColor = new Color(0.72f, 0.45f, 0.95f);
        private static readonly Color BuffColor = new Color(1f, 0.84f, 0.28f);

        private readonly RectTransform _root;
        private readonly Slot[] _slots;
        // 外部推送的狀態到期時間(依 _slots 順序)
        private readonly float[] _pushedUntil;

        // 這一幀顯示中的狀態(測試/除錯)
        public StatusFlags Shown { get; private set; }

        public RectTransform Root => _root;

        public StatusIconRow(Transform parent, Vector2 anchor, Vector2 position)
        {
            _root = Ui.Rect("StatusIcons", parent, anchor, position, new Vector2(Spacing * 3f, SlotSize + 30f));
            _slots = new[]
            {
                CreateSlot(StatusFlags.Marked, "被標記!", UiSprites.TrackedEye()),
                CreateSlot(StatusFlags.Slowed, "減速", UiSprites.Chevrons(false)),
                CreateSlot(StatusFlags.Buffed, "強化", UiSprites.Chevrons(true)),
            };
            _pushedUntil = new float[_slots.Length];
        }

        private Slot CreateSlot(StatusFlags flag, string label, Sprite icon)
        {
            var slot = new Slot { Flag = flag };
            // 錨點左上:圖示本體在上、說明文字在下
            slot.Root = Ui.Rect("Status_" + flag, _root, new Vector2(0f, 1f), Vector2.zero, new Vector2(SlotSize, SlotSize + 30f));
            // 中心頂端為軸:彈出動畫從圖示中心放大
            slot.Root.pivot = new Vector2(0.5f, 1f);
            slot.Back = Ui.Image("Back", slot.Root, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(SlotSize, SlotSize),
                UiSprites.Circle(), BackColor);
            slot.Timer = Ui.Image("Timer", slot.Root, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(SlotSize, SlotSize),
                UiSprites.SegmentedRing(128, 10f, 0, 0f), Color.white);
            slot.Timer.type = Image.Type.Filled;
            slot.Timer.fillMethod = Image.FillMethod.Radial360;
            slot.Timer.fillOrigin = (int)Image.Origin360.Top;
            slot.Timer.fillClockwise = false;
            slot.Icon = Ui.Image("Icon", slot.Root, new Vector2(0.5f, 1f), new Vector2(0f, -SlotSize * 0.2f),
                new Vector2(SlotSize * 0.6f, SlotSize * 0.6f), icon, Color.white);
            slot.Label = Ui.Text("Label", slot.Root, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(SlotSize * 2f, 28f), 22,
                TextAnchor.MiddleCenter, Color.white, true, 2f);
            slot.Label.text = label;
            slot.Root.gameObject.SetActive(false);
            return slot;
        }

        // 外部推送一個狀態顯示 seconds 秒(沒有對應效果/標記的臨時提示,例:被 Ink Storm 淋到)
        public void Push(StatusFlags flags, float seconds)
        {
            float until = Time.time + Mathf.Max(0f, seconds);
            for (int i = 0; i < _slots.Length; i++)
            {
                if ((flags & _slots[i].Flag) != 0)
                {
                    _pushedUntil[i] = Mathf.Max(_pushedUntil[i], until);
                }
            }
        }

        public bool IsShown(StatusFlags flag)
        {
            return (Shown & flag) != 0;
        }

        // 每幀:依本機玩家狀態顯示/隱藏(player 為 null 或 hidden = 全部收起)
        public void Update(Inkling player, bool hidden)
        {
            StatusFlags flags = StatusFlags.None;
            float markedRemaining = 0f;
            if (player != null && !hidden)
            {
                markedRemaining = Marking.RemainingByEnemy(player);
                if (markedRemaining > 0f)
                {
                    flags |= StatusFlags.Marked;
                }
                flags |= player.Modifiers.Status;
                float now = Time.time;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_pushedUntil[i] > now)
                    {
                        flags |= _slots[i].Flag;
                    }
                }
            }
            Shown = flags;

            Color enemy = player != null ? TeamPalette.ColorOf(TeamUtil.Opponent(player.Team)) : Color.white;
            int column = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot slot = _slots[i];
                bool on = (flags & slot.Flag) != 0;
                if (on != slot.Shown)
                {
                    slot.Shown = on;
                    slot.ShownAt = Time.time;
                    slot.Total = 0f;
                    slot.Root.gameObject.SetActive(on);
                }
                if (!on)
                {
                    continue;
                }
                slot.Root.anchoredPosition = new Vector2(column * Spacing + SlotSize * 0.5f, 0f);
                column++;
                float age = Time.time - slot.ShownAt;
                slot.Root.localScale = Vector3.one * Ui.PopScale(Mathf.Clamp01(age * 4f));
                UpdateSlotLook(slot, enemy, markedRemaining);
            }
        }

        private static void UpdateSlotLook(Slot slot, Color enemy, float markedRemaining)
        {
            switch (slot.Flag)
            {
                case StatusFlags.Marked:
                {
                    // 外圈倒數:以這次顯示期間看過的最長剩餘時間為滿(重新標記會刷新)
                    slot.Total = Mathf.Max(slot.Total, markedRemaining);
                    slot.Timer.fillAmount = slot.Total > 0f ? markedRemaining / slot.Total : 0f;
                    float blink = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 6f));
                    slot.Icon.color = Color.Lerp(Color.white, enemy, 0.35f);
                    slot.Timer.color = Ui.WithAlpha(enemy, 0.95f);
                    slot.Back.color = Ui.WithAlpha(Color.Lerp(Color.black, enemy, 0.35f), 0.75f);
                    slot.Label.color = Ui.WithAlpha(Color.Lerp(Color.white, enemy, 0.4f), blink);
                    break;
                }
                case StatusFlags.Slowed:
                    slot.Timer.fillAmount = 1f;
                    slot.Timer.color = Ui.WithAlpha(SlowColor, 0.9f);
                    slot.Icon.color = SlowColor;
                    slot.Back.color = BackColor;
                    slot.Label.color = Color.white;
                    break;
                default:
                    slot.Timer.fillAmount = 1f;
                    slot.Timer.color = Ui.WithAlpha(BuffColor, 0.5f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 3f)));
                    slot.Icon.color = BuffColor;
                    slot.Back.color = BackColor;
                    slot.Label.color = Color.white;
                    break;
            }
        }
    }
}
