(function () {
    const KEY = 'vps_theme';
    const root = document.documentElement;
    function applyButtonState(theme) {
        document.querySelectorAll('.theme-toggle-btn').forEach(button => {
            button.dataset.themeState = theme;
            button.setAttribute(
                'title',
                theme === 'dark' ? 'الوضع الليلي مفعّل - اضغط للتبديل' : 'الوضع النهاري مفعّل - اضغط للتبديل'
            );
            button.setAttribute(
                'aria-label',
                theme === 'dark' ? 'الوضع الليلي مفعّل - اضغط للتبديل' : 'الوضع النهاري مفعّل - اضغط للتبديل'
            );
        });
    }

    function apply(theme, options) {
        const useTransition = options?.useTransition === true;

        if (useTransition) {
            root.classList.add('theme-transition');
        }

        root.setAttribute('data-theme', theme);
        root.style.backgroundColor = theme === 'dark' ? '#050914' : '#f8fafc';
        root.style.colorScheme = theme;
        applyButtonState(theme);

        if (useTransition) {
            requestAnimationFrame(() => {
                setTimeout(() => root.classList.remove('theme-transition'), 400);
            });
        }
    }

    function stored() {
        var v = localStorage.getItem(KEY);
        if (v === 'night' || v === 'dark') return 'dark';
        return 'light';
    }

    function init() {
        apply(stored(), { useTransition: false });
        document.addEventListener('click', function (e) {
            if (!e.target.closest('.theme-toggle-btn')) return;
            var current = root.getAttribute('data-theme');
            var next = current === 'dark' ? 'light' : 'dark';
            localStorage.setItem(KEY, next);
            apply(next, { useTransition: true });
        });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
