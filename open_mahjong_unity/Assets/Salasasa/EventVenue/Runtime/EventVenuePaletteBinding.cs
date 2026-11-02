using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Salasasa.Events
{
    /// <summary>Authored colors for a shared view or a pooled row; clone references remain local to the row.</summary>
    public sealed class EventVenuePaletteBinding : MonoBehaviour
    {
        public enum Property { Graphic, ShapeBorder, ActionIdle, ActionActive, ActionIdleInk, ActionActiveInk, ActionDisabledFill, ActionDisabledInk, ActionDetailIdle, ActionCompletedFill, ActionCompletedInk, BindingNormalInk, BindingPositiveInk, BindingPendingInk, BindingNegativeInk, BindingNeutralInk, ViewPaper, ViewInk, ViewMuted, ViewAccent, ViewOnAccent, ViewBorder, ViewField, InputCaret, InputSelection, Shadow }
        [Serializable]
        public struct Entry
        {
            public Component target;
            public Property property;
            public Color[] colors;
        }
        public Entry[] entries = Array.Empty<Entry>();

        public void Apply(int palette)
        {
            foreach (var entry in entries)
            {
                if (!entry.target || entry.colors == null || entry.colors.Length == 0) continue;
                var color = entry.colors[Mathf.Clamp(palette, 0, entry.colors.Length - 1)];
                switch (entry.property)
                {
                    case Property.Graphic: ((Graphic)entry.target).color = color; break;
                    case Property.ShapeBorder: ((EventVenueShape)entry.target).borderColor = color; break;
                    case Property.ActionIdle: ((EventVenueAction)entry.target).idle = color; break;
                    case Property.ActionActive: ((EventVenueAction)entry.target).active = color; break;
                    case Property.ActionIdleInk: ((EventVenueAction)entry.target).idleInk = color; break;
                    case Property.ActionActiveInk: ((EventVenueAction)entry.target).activeInk = color; break;
                    case Property.ActionDisabledFill: ((EventVenueAction)entry.target).disabledFill = color; break;
                    case Property.ActionDisabledInk: ((EventVenueAction)entry.target).disabledInk = color; break;
                    case Property.ActionDetailIdle: ((EventVenueAction)entry.target).detailIdle = color; break;
                    case Property.ActionCompletedFill: ((EventVenueAction)entry.target).completedFill = color; break;
                    case Property.ActionCompletedInk: ((EventVenueAction)entry.target).completedInk = color; break;
                    case Property.BindingNormalInk: ((EventVenueBinding)entry.target).normalInk = color; break;
                    case Property.BindingPositiveInk: ((EventVenueBinding)entry.target).positiveInk = color; break;
                    case Property.BindingPendingInk: ((EventVenueBinding)entry.target).pendingInk = color; break;
                    case Property.BindingNegativeInk: ((EventVenueBinding)entry.target).negativeInk = color; break;
                    case Property.BindingNeutralInk: ((EventVenueBinding)entry.target).neutralInk = color; break;
                    case Property.ViewPaper: ((EventVenueView)entry.target).paper = color; break;
                    case Property.ViewInk: ((EventVenueView)entry.target).ink = color; break;
                    case Property.ViewMuted: ((EventVenueView)entry.target).muted = color; break;
                    case Property.ViewAccent: ((EventVenueView)entry.target).accent = color; break;
                    case Property.ViewOnAccent: ((EventVenueView)entry.target).onAccent = color; break;
                    case Property.ViewBorder: ((EventVenueView)entry.target).border = color; break;
                    case Property.ViewField: ((EventVenueView)entry.target).field = color; break;
                    case Property.InputCaret: ((TMP_InputField)entry.target).caretColor = color; break;
                    case Property.InputSelection: ((TMP_InputField)entry.target).selectionColor = color; break;
                    case Property.Shadow: ((Shadow)entry.target).effectColor = color; break;
                }
                if (entry.target is Graphic graphic) graphic.SetVerticesDirty();
            }
        }

#if UNITY_EDITOR
        public Color Read(Entry entry)
        {
            switch (entry.property)
            {
                case Property.Graphic: return ((Graphic)entry.target).color;
                case Property.ShapeBorder: return ((EventVenueShape)entry.target).borderColor;
                case Property.ActionIdle: return ((EventVenueAction)entry.target).idle;
                case Property.ActionActive: return ((EventVenueAction)entry.target).active;
                case Property.ActionIdleInk: return ((EventVenueAction)entry.target).idleInk;
                case Property.ActionActiveInk: return ((EventVenueAction)entry.target).activeInk;
                case Property.ActionDisabledFill: return ((EventVenueAction)entry.target).disabledFill;
                case Property.ActionDisabledInk: return ((EventVenueAction)entry.target).disabledInk;
                case Property.ActionDetailIdle: return ((EventVenueAction)entry.target).detailIdle;
                case Property.ActionCompletedFill: return ((EventVenueAction)entry.target).completedFill;
                case Property.ActionCompletedInk: return ((EventVenueAction)entry.target).completedInk;
                case Property.BindingNormalInk: return ((EventVenueBinding)entry.target).normalInk;
                case Property.BindingPositiveInk: return ((EventVenueBinding)entry.target).positiveInk;
                case Property.BindingPendingInk: return ((EventVenueBinding)entry.target).pendingInk;
                case Property.BindingNegativeInk: return ((EventVenueBinding)entry.target).negativeInk;
                case Property.BindingNeutralInk: return ((EventVenueBinding)entry.target).neutralInk;
                case Property.ViewPaper: return ((EventVenueView)entry.target).paper;
                case Property.ViewInk: return ((EventVenueView)entry.target).ink;
                case Property.ViewMuted: return ((EventVenueView)entry.target).muted;
                case Property.ViewAccent: return ((EventVenueView)entry.target).accent;
                case Property.ViewOnAccent: return ((EventVenueView)entry.target).onAccent;
                case Property.ViewBorder: return ((EventVenueView)entry.target).border;
                case Property.ViewField: return ((EventVenueView)entry.target).field;
                case Property.InputCaret:
                    // TMP returns the text color while customCaretColor is off; retain the authored caret value.
                    using (var data = new UnityEditor.SerializedObject(entry.target))
                        return data.FindProperty("m_CaretColor").colorValue;
                case Property.InputSelection: return ((TMP_InputField)entry.target).selectionColor;
                case Property.Shadow: return ((Shadow)entry.target).effectColor;
                default: throw new ArgumentOutOfRangeException();
            }
        }
        public void Capture(int palette)
        {
            foreach (var entry in entries) entry.colors[palette] = Read(entry);
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
