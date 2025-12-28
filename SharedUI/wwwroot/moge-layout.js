(function () {
    function setFooterHeightVar(footerEl) {
        try {
            if (!footerEl)
                return;

            var h = footerEl.offsetHeight || 0;
            // add a small gap so content isn't flush against the footer border
            var px = (h + 8) + 'px';
            document.documentElement.style.setProperty('--moge-footer-height', px);
        }
        catch {
        }
    }

    function preventWheelScroll(el) {
        try {
            if (!el)
                return;

            if (el.__mogeWheelGuardInstalled)
                return;

            el.addEventListener('wheel', function (e) {
                // Prevent the outer page from scrolling while the cursor is over the canvas.
                // Don't stop propagation so Blazor's `@onwheel` handler can still run (zoom).
                // Also allow Ctrl/Meta+wheel browser zoom behavior.
                if (e && (e.ctrlKey || e.metaKey))
                    return;

                try {
                    e.preventDefault();
                } catch {
                }
            }, { passive: false });

            el.__mogeWheelGuardInstalled = true;
        }
        catch {
        }
    }

    window.mogeLayout = window.mogeLayout || {};
    window.mogeLayout.setFooterHeightVar = setFooterHeightVar;
    window.mogeLayout.preventWheelScroll = preventWheelScroll;
})();
