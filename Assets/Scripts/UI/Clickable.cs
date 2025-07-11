using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace ridorana.IC10Inspector.UI {
    public class Clickable : UIBehaviour, IPointerClickHandler, IPointerMoveHandler, IPointerExitHandler {
        
        
        // Event delegates triggered on click.
        [SerializeField]
        private UnityEvent<Vector2> m_OnClick = new();
        
        [SerializeField]
        private UnityEvent<Vector2> m_OnMove = new();
        
        [SerializeField]
        private UnityEvent m_OnExit = new();
        
        protected Clickable()
        {}
        
        public void OnPointerClick(PointerEventData eventData) {
            if (eventData.button != PointerEventData.InputButton.Left) {
                return;
            }

            Press(eventData.position);
        }
        
        [Tooltip("Can the this be interacted with?")]
        [SerializeField]
        private bool m_Interactable = true;
        
        public virtual bool IsInteractable()
        {
            return m_Interactable;
        }
        
        private void Press(Vector2 position)
        {
            if (!IsActive() || !IsInteractable())
                return;

            UISystemProfilerApi.AddMarker("Clickable.onClick", this);
            m_OnClick.Invoke(position);
        }
        
        private void Move(Vector2 position)
        {
            if (!IsActive() || !IsInteractable())
                return;

            UISystemProfilerApi.AddMarker("Clickable.onMove", this);
            m_OnMove.Invoke(position);
        }
        
        private void Exit()
        {
            if (!IsActive() || !IsInteractable())
                return;

            UISystemProfilerApi.AddMarker("Clickable.onMove", this);
            m_OnExit.Invoke();
        }

        public void OnPointerMove(PointerEventData eventData) {
            Move(eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData) {
            Exit();
        }
    }
}