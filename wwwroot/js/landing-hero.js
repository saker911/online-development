(() => {
    const hero = document.querySelector(".public-landing-hero");
    const visual = hero?.querySelector("[data-landing-hero-visual]");
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
    const coarsePointer = window.matchMedia("(pointer: coarse)");

    if (!hero || !visual || reducedMotion.matches || coarsePointer.matches) {
        return;
    }

    let frame = 0;

    const updatePosition = (event) => {
        const bounds = hero.getBoundingClientRect();
        const x = ((event.clientX - bounds.left) / bounds.width) - 0.5;
        const y = ((event.clientY - bounds.top) / bounds.height) - 0.5;

        cancelAnimationFrame(frame);
        frame = requestAnimationFrame(() => {
            visual.style.setProperty("--hero-shift-x", `${(x * 10).toFixed(2)}px`);
            visual.style.setProperty("--hero-shift-y", `${(y * 7).toFixed(2)}px`);
        });
    };

    const resetPosition = () => {
        cancelAnimationFrame(frame);
        visual.style.setProperty("--hero-shift-x", "0px");
        visual.style.setProperty("--hero-shift-y", "0px");
    };

    hero.addEventListener("pointermove", updatePosition, { passive: true });
    hero.addEventListener("pointerleave", resetPosition, { passive: true });
})();
