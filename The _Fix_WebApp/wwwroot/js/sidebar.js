// Collapsible sidebar: desktop collapse (remembered), mobile drawer.
(function () {
    var root = document.documentElement;
    var KEY = 'fashionfix:sidebar-collapsed';

    function setCollapsed(on) {
        root.classList.toggle('sn-collapsed', on);
        try { localStorage.setItem(KEY, on ? '1' : '0'); } catch (e) { /* storage blocked: still works for this page */ }
        var btn = document.getElementById('snCollapse');
        if (btn) {
            btn.setAttribute('aria-expanded', on ? 'false' : 'true');
            btn.setAttribute('aria-label', on ? 'Expand sidebar' : 'Collapse sidebar');
            btn.title = on ? 'Expand sidebar' : 'Collapse sidebar';
        }
    }

    function closeDrawer() { root.classList.remove('sn-drawer'); }

    document.addEventListener('DOMContentLoaded', function () {
        var collapsed = false;
        try { collapsed = localStorage.getItem(KEY) === '1'; } catch (e) { }
        setCollapsed(collapsed);

        var collapseBtn = document.getElementById('snCollapse');
        if (collapseBtn) collapseBtn.addEventListener('click', function () {
            setCollapsed(!root.classList.contains('sn-collapsed'));
        });

        document.querySelectorAll('.sn-open-btn').forEach(function (b) {
            b.addEventListener('click', function () { root.classList.add('sn-drawer'); });
        });

        var backdrop = document.getElementById('snBackdrop');
        if (backdrop) backdrop.addEventListener('click', closeDrawer);
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeDrawer(); });

        // Tapping a link inside the drawer should close it.
        document.querySelectorAll('.sn .sn-link').forEach(function (a) {
            a.addEventListener('click', closeDrawer);
        });
    });
})();
