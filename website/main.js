(function () {
    window.__momokaReady = true;

    var body = document.body;
    var preview = document.querySelector('.theme-preview');
    var nameEl = document.getElementById('theme-name');
    var statusEl = document.getElementById('theme-status');
    var swatches = Array.prototype.slice.call(document.querySelectorAll('.swatch'));
    var themes = {
        forest: '墨绿',
        black: '纯黑',
        midnight: '午夜',
        graphite: '石墨',
        violet: '紫夜'
    };

    function setTheme(theme, persist) {
        if (!Object.prototype.hasOwnProperty.call(themes, theme)) {
            theme = 'forest';
        }
        body.setAttribute('data-theme', theme);
        if (preview) {
            preview.setAttribute('data-theme', theme);
        }
        if (nameEl) {
            nameEl.textContent = themes[theme];
        }
        if (statusEl) {
            statusEl.textContent = '当前主题：' + themes[theme];
        }
        swatches.forEach(function (button) {
            button.setAttribute('aria-pressed', String(button.getAttribute('data-set-theme') === theme));
        });
        if (persist) {
            try {
                localStorage.setItem('momoka-theme', theme);
            } catch (error) {
                /* 本地存储不可用时只切换当前页面 */
            }
        }
    }

    var savedTheme = null;
    try {
        savedTheme = localStorage.getItem('momoka-theme');
    } catch (error) {
        savedTheme = null;
    }
    setTheme(savedTheme || 'forest', false);

    swatches.forEach(function (button) {
        button.addEventListener('click', function () {
            setTheme(button.getAttribute('data-set-theme'), true);
        });
    });

    var header = document.querySelector('.site-header');

    function syncHeader() {
        if (!header) {
            return;
        }
        header.classList.toggle('is-scrolled', window.scrollY > 8);
    }

    syncHeader();
    window.addEventListener('scroll', syncHeader, { passive: true });

    var revealItems = Array.prototype.slice.call(document.querySelectorAll('.reveal'));
    var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    if (reduceMotion || !('IntersectionObserver' in window)) {
        revealItems.forEach(function (item) {
            item.classList.add('is-visible');
        });
    } else {
        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add('is-visible');
                    observer.unobserve(entry.target);
                }
            });
        }, { threshold: 0.1, rootMargin: '0px 0px -6% 0px' });

        revealItems.forEach(function (item) {
            observer.observe(item);
        });
    }
})();
