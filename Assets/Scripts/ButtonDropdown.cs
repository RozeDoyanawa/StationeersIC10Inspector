using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ridorana.IC10Inspector
{
    public class ButtonDropdown : global::UI.Dropdown.ButtonDropdown
    {
        [SerializeField]
        private UnityEvent<Int32> m_OnValueChanged = new();
        
        public new void ItemClicked(int index) {
            base.ItemClicked(index);
            m_OnValueChanged.Invoke(index);
        }
        
    }
}
