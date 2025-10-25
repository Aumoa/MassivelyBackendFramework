export function registerOutsideClick(dotnetRef, panelId) {
    function handler(e) {
        const panel = document.getElementById(panelId);
        if (panel && !panel.contains(e.target)) {
            dotnetRef.invokeMethodAsync('OnOutsideClick');
        }
    }
    document.addEventListener('mousedown', handler);
    return {
        dispose: () => document.removeEventListener('mousedown', handler)
    };
}