(() => {
    "use strict";

    const alphaThreshold = 16;

    function findVisibleBounds(image) {
        const maxSampleSize = 256;
        const sampleScale = Math.min(
            1,
            maxSampleSize / Math.max(image.naturalWidth, image.naturalHeight)
        );
        const width = Math.max(1, Math.round(image.naturalWidth * sampleScale));
        const height = Math.max(1, Math.round(image.naturalHeight * sampleScale));
        const canvas = document.createElement("canvas");
        const context = canvas.getContext("2d", { willReadFrequently: true });
        if (!context) {
            return null;
        }

        canvas.width = width;
        canvas.height = height;
        context.drawImage(image, 0, 0, width, height);
        const pixels = context.getImageData(0, 0, width, height).data;
        let left = width;
        let right = -1;
        let top = height;
        let bottom = -1;

        for (let y = 0; y < height; y += 1) {
            for (let x = 0; x < width; x += 1) {
                const alpha = pixels[(y * width + x) * 4 + 3];
                if (alpha <= alphaThreshold) {
                    continue;
                }

                left = Math.min(left, x);
                right = Math.max(right, x);
                top = Math.min(top, y);
                bottom = Math.max(bottom, y);
            }
        }

        if (right < left || bottom < top) {
            return null;
        }

        const sourceScaleX = image.naturalWidth / width;
        const sourceScaleY = image.naturalHeight / height;
        return {
            x: left * sourceScaleX,
            y: top * sourceScaleY,
            width: (right - left + 1) * sourceScaleX,
            height: (bottom - top + 1) * sourceScaleY
        };
    }

    function fitLogo(frame, image) {
        if (!image.naturalWidth || !image.naturalHeight) {
            return;
        }

        try {
            const bounds = findVisibleBounds(image);
            if (!bounds) {
                return;
            }

            const visualAspect = bounds.width / bounds.height;
            frame.classList.toggle("is-wide-brand-logo", visualAspect > 1.55);

            const frameWidth = frame.clientWidth;
            const frameHeight = frame.clientHeight;
            const inset = 6;
            const scale = Math.min(
                (frameWidth - inset) / bounds.width,
                (frameHeight - inset) / bounds.height
            );
            const visibleWidth = bounds.width * scale;
            const visibleHeight = bounds.height * scale;

            image.style.width = `${image.naturalWidth * scale}px`;
            image.style.height = `${image.naturalHeight * scale}px`;
            image.style.left = `${(frameWidth - visibleWidth) / 2 - bounds.x * scale}px`;
            image.style.top = `${(frameHeight - visibleHeight) / 2 - bounds.y * scale}px`;
            image.classList.add("is-brand-logo-fitted");
        } catch {
            image.classList.remove("is-brand-logo-fitted");
        }
    }

    document.querySelectorAll("[data-brand-logo-frame]").forEach((frame) => {
        const image = frame.querySelector("img");
        if (!image) {
            return;
        }

        const apply = () => fitLogo(frame, image);
        if (image.complete) {
            apply();
        } else {
            image.addEventListener("load", apply, { once: true });
        }

        window.addEventListener("resize", apply, { passive: true });
    });
})();
