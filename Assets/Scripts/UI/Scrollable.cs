using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace ridorana.IC10Inspector.UI {
    public class Scrollable : UIBehaviour, IScrollHandler {
        
        
        // Event delegates triggered on click.
        [SerializeField]
        private UnityEvent<Vector2> m_OnScroll = new();
        
        protected Scrollable()
        {}

        public void OnScroll(PointerEventData eventData) {
            Scroll(eventData.scrollDelta);
        }
        
        
        [Tooltip("Can the this be interacted with?")]
        [SerializeField]
        private bool m_Interactable = true;
        
        public virtual bool IsInteractable()
        {
            return m_Interactable;
        }

        private void Scroll(Vector2 scrollDelta) {
            if (!IsActive() || !IsInteractable())
                return;
            
            UISystemProfilerApi.AddMarker("Scrollable.onScroll", this);
            m_OnScroll.Invoke(scrollDelta);
        }
        
    }
}