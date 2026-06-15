(function () {
    const KEY = 'vps_theme';
    const root = document.documentElement;
    const ICONS = {
        dark: '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M14.16 2.25c.3 0 .56.21.62.5.06.29-.1.59-.37.7a8.48 8.48 0 0 0-5.28 7.9c0 3.73 2.44 7.03 6 8.12.3.09.49.38.47.69a.68.68 0 0 1-.58.61c-.47.06-.95.1-1.43.1-5.45 0-9.87-4.42-9.87-9.87 0-4.8 3.44-8.92 8.16-9.79.09-.02.18-.03.28-.05.8-.11 1.6-.05 2.4.16Z" fill="currentColor"/><path d="M17.78 5.38l.27.84c.1.31.34.56.66.66l.84.27-.84.27a1.06 1.06 0 0 0-.66.66l-.27.84-.27-.84a1.06 1.06 0 0 0-.66-.66l-.84-.27.84-.27c.31-.1.56-.34.66-.66l.27-.84Z" fill="currentColor" opacity="0.82"/></svg>',
        light: '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><circle cx="12" cy="12" r="4.5" fill="currentColor"/><path d="M12 2.75v2.1M12 19.15v2.1M21.25 12h-2.1M4.85 12h-2.1M18.54 5.46l-1.48 1.48M6.94 17.06l-1.48 1.48M18.54 18.54l-1.48-1.48M6.94 6.94L5.46 5.46" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>'
    };

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

        document.querySelectorAll('.theme-toggle-icon').forEach(el => {
            el.innerHTML = theme === 'dark' ? ICONS.dark : ICONS.light;
        });

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
