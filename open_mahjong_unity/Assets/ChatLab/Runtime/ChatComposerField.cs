using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Salasasa.ChatLab
{
    public sealed class ChatComposerField : TMP_InputField
    {
        readonly Event keyEvent=new Event();
        public override void OnUpdateSelected(BaseEventData eventData)
        {
            if(!isFocused||!IsInteractable())return;
            bool consumed=false;
            while(Event.PopEvent(keyEvent)){
                if(keyEvent.rawType==EventType.KeyDown){
                    consumed=true;
                    bool enter=keyEvent.keyCode==KeyCode.Return||keyEvent.keyCode==KeyCode.KeypadEnter;
#if ENABLE_LEGACY_INPUT_MANAGER
                    if(enter&&Input.compositionString.Length>0)continue;
#endif
                    // Use the modifiers attached to this key event, even if Shift was released before the frame.
                    if(enter&&keyEvent.shift){
                        if(readOnly)continue;
                        int start=Mathf.Min(selectionStringAnchorPosition,selectionStringFocusPosition);
                        int end=Mathf.Max(selectionStringAnchorPosition,selectionStringFocusPosition);
                        string value=text.Remove(start,end-start);
                        if(characterLimit==0||value.Length<characterLimit){text=value.Insert(start,"\n");stringPosition=start+1;}
                        continue;
                    }
                    if(KeyPressed(keyEvent)==EditState.Finish){
                        if(!wasCanceled)SendOnSubmit();
                        DeactivateInputField();break;
                    }
                }
                else if((keyEvent.rawType==EventType.ValidateCommand||keyEvent.rawType==EventType.ExecuteCommand)&&keyEvent.commandName=="SelectAll"){
                    SelectAll();consumed=true;
                }
            }
#if ENABLE_LEGACY_INPUT_MANAGER
            // Keep TMP's IME event protection (UUM-100552), including frames without a KeyDown event.
            consumed |= !readOnly && Input.compositionString.Length>0;
#endif
            if(consumed){ForceLabelUpdate();textComponent.ForceMeshUpdate();eventData.Use();}
        }
    }
}
