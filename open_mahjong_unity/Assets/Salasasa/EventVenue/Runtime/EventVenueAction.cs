using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Salasasa.Events
{
    [RequireComponent(typeof(Button))]
    public sealed class EventVenueAction : MonoBehaviour
    {
        public EventVenuePanel controller;
        public string action;
        public int index;
        public Button button;
        public Graphic fill;
        public TMP_Text label;
        public TMP_Text iconLabel;
        public Color idle, active, idleInk, activeInk;
        public Color disabledFill, disabledInk;
        public TMP_Text[] detailLabels;
        public Color detailIdle;
        public bool ownerOnly;
        public bool preserveAppearance;
        public bool useStateColors;
        public Color completedFill, completedInk;
        void Awake() { button.onClick.AddListener(Click); }
        void OnDestroy() { if (button != null) button.onClick.RemoveListener(Click); }
        public void Click() { if (button.interactable) controller.Execute(action, index); }
        public void Refresh()
        {
            if(preserveAppearance){button.interactable=controller.CanExecute(action,index);return;}
            bool selected = controller.IsSelected(action, index);
            if (fill != null) fill.color = selected ? active : idle;
            if (label != null) label.color = selected ? activeInk : idleInk;
            if (iconLabel != null) iconLabel.color = selected ? activeInk : idleInk;
            if (detailLabels != null) foreach(var detail in detailLabels) if(detail!=null) detail.color=selected?activeInk:detailIdle;
            button.interactable = (!ownerOnly || controller.Role == 2) && controller.CanExecute(action, index);
            if (!button.interactable)
            {
                if(fill!=null)fill.color=disabledFill;
                if(label!=null)label.color=disabledInk;
                if(iconLabel!=null)iconLabel.color=disabledInk;
            }
            if(useStateColors && ((action=="register" && controller.Read("register",index)=="已报名") || (action=="ready" && selected)))
            {
                if(fill)fill.color=completedFill;
                if(label)label.color=completedInk;
                if(iconLabel)iconLabel.color=completedInk;
            }
        }
    }
}
