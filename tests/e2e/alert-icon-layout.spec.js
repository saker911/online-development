const { expect, test } = require("@playwright/test");
const { ensureOwnerSignedIn, owner, signOut } = require("./helpers/e2e-helpers");

const longRtlMessage =
    "هذه رسالة تنبيه طويلة باللغة العربية للتأكد من أن الأيقونة لا تغطي بداية النص، وأن النص يلتف على أكثر من سطر داخل مساحة ضيقة مع بقاء كامل المحتوى ظاهرًا في اتجاه RTL دون قص أو تداخل.";

async function assertPseudoIconAlertLayout(locator, options = {}) {
    await expect(locator).toBeVisible();
    const result = await locator.evaluate((element) => {
        const parsePixels = (value) => {
            const parsed = Number.parseFloat(value);
            return Number.isFinite(parsed) ? parsed : null;
        };

        const elementRect = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        const before = getComputedStyle(element, "::before");
        const beforeWidth = parsePixels(before.width) ?? 0;
        const beforeHeight = parsePixels(before.height) ?? 0;
        const beforeTop = parsePixels(before.top) ?? 0;
        const beforeRight = parsePixels(before.right);
        const beforeLeft = parsePixels(before.left);
        const iconLeft = beforeRight !== null
            ? elementRect.right - beforeRight - beforeWidth
            : elementRect.left + (beforeLeft ?? 0);
        const iconRect = {
            left: iconLeft,
            right: iconLeft + beforeWidth,
            top: elementRect.top + beforeTop,
            bottom: elementRect.top + beforeTop + beforeHeight,
        };

        const textRects = [];
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT, {
            acceptNode(node) {
                return node.nodeValue.trim().length > 0
                    ? NodeFilter.FILTER_ACCEPT
                    : NodeFilter.FILTER_REJECT;
            },
        });

        while (walker.nextNode()) {
            const range = document.createRange();
            range.selectNodeContents(walker.currentNode);
            for (const rect of range.getClientRects()) {
                if (rect.width > 1 && rect.height > 1) {
                    textRects.push({
                        left: rect.left,
                        right: rect.right,
                        top: rect.top,
                        bottom: rect.bottom,
                        width: rect.width,
                        height: rect.height,
                    });
                }
            }
            range.detach();
        }

        return {
            direction: style.direction,
            display: style.display,
            columnGap: parsePixels(style.columnGap) ?? 0,
            beforeContent: before.content,
            beforePosition: before.position,
            inlineStartPadding: parsePixels(style.paddingInlineStart) ?? 0,
            iconWidth: beforeWidth,
            hasHorizontalOverflow: element.scrollWidth > element.clientWidth + 1,
            hasVerticalClip: element.scrollHeight > element.clientHeight + 2,
            lineCount: new Set(textRects.map((rect) => Math.round(rect.top))).size,
            overlapCount: textRects.filter((rect) => {
                return rect.left < iconRect.right && rect.right > iconRect.left && rect.top < iconRect.bottom && rect.bottom > iconRect.top;
            }).length,
        };
    });

    expect(result.direction).toBe("rtl");
    expect(result.beforeContent).not.toBe("none");
    if (result.beforePosition === "absolute") {
        expect(result.inlineStartPadding).toBeGreaterThanOrEqual(result.iconWidth + 12);
        expect(result.overlapCount).toBe(0);
    } else {
        expect(result.display).toBe("flex");
        expect(result.columnGap).toBeGreaterThan(0);
    }
    expect(result.hasHorizontalOverflow).toBe(false);
    expect(result.hasVerticalClip).toBe(false);
    if (options.expectWrapped) {
        expect(result.lineCount).toBeGreaterThan(1);
    }
}

async function assertRealIconAlertLayout(locator, options = {}) {
    await expect(locator).toBeVisible();
    const result = await locator.evaluate((element) => {
        const rectanglesOverlap = (first, second) => {
            return first.left < second.right && first.right > second.left && first.top < second.bottom && first.bottom > second.top;
        };
        const icon = element.querySelector(".app-notify-icon");
        const text = element.querySelector(".app-notify-text");
        if (!icon || !text) {
            throw new Error("Notification alert must include icon and text elements.");
        }

        const iconRect = icon.getBoundingClientRect();
        const textRect = text.getBoundingClientRect();
        const range = document.createRange();
        range.selectNodeContents(text);
        const textLineRects = Array.from(range.getClientRects())
            .filter((rect) => rect.width > 1 && rect.height > 1)
            .map((rect) => ({ left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom }));
        range.detach();

        return {
            direction: getComputedStyle(element).direction,
            hasHorizontalOverflow: text.scrollWidth > text.clientWidth + 1,
            hasVerticalClip: text.scrollHeight > text.clientHeight + 2,
            lineCount: new Set(textLineRects.map((rect) => Math.round(rect.top))).size,
            overlapCount: textLineRects.filter((rect) => rectanglesOverlap(rect, iconRect)).length,
            textIsInsideContainer: textRect.left >= element.getBoundingClientRect().left && textRect.right <= element.getBoundingClientRect().right,
        };
    });

    expect(result.direction).toBe("rtl");
    expect(result.hasHorizontalOverflow).toBe(false);
    expect(result.hasVerticalClip).toBe(false);
    expect(result.overlapCount).toBe(0);
    expect(result.textIsInsideContainer).toBe(true);
    if (options.expectWrapped) {
        expect(result.lineCount).toBeGreaterThan(1);
    }
}

async function setTheme(page, theme) {
    await page.goto("/Account/Login");
    await page.evaluate((nextTheme) => localStorage.setItem("vps_theme", nextTheme), theme);
}

async function addAlertFixtures(page) {
    await page.evaluate((message) => {
        const existing = document.getElementById("alert-icon-layout-fixtures");
        if (existing) {
            existing.remove();
        }

        const host = document.createElement("section");
        host.id = "alert-icon-layout-fixtures";
        host.setAttribute("aria-label", "اختبار تنسيق التنبيهات");
        host.style.cssText = "width:min(430px, calc(100vw - 2rem));margin:1rem auto;display:grid;gap:0.75rem;";
        host.innerHTML = `
      <div class="app-inline-note app-inline-note-info" data-alert-fixture="inline-info">${message}</div>
      <div class="app-inline-note app-inline-note-warning" data-alert-fixture="inline-warning">${message}</div>
      <div class="app-inline-note app-inline-note-danger" data-alert-fixture="inline-danger">${message}</div>
      <div class="app-inline-note app-inline-note-success" data-alert-fixture="inline-success">${message}</div>
      <div class="alert alert-warning" role="alert" data-alert-fixture="bootstrap-warning">${message}</div>
      <div class="app-notify app-notify-success" role="alert" data-alert-fixture="notify-success">
        <div class="app-notify-icon" aria-hidden="true">✓</div>
        <div class="app-notify-text">${message}</div>
        <button class="app-notify-close" aria-label="إغلاق" type="button">✕</button>
        <div class="app-notify-bar" aria-hidden="true"></div>
      </div>`;

        const target = document.querySelector("main") ?? document.body;
        target.prepend(host);
    }, longRtlMessage);
}

test("RTL alert icons do not cover wrapped text in light and dark modes", async ({ page }) => {
    await page.setViewportSize({ width: 500, height: 900 });

    for (const theme of ["light", "dark"]) {
        await setTheme(page, theme);
        await ensureOwnerSignedIn(page);
        await page.goto("/Administration/Leadership");
        await expect(page.locator("html")).toHaveAttribute("data-theme", theme);

        await assertPseudoIconAlertLayout(page.locator(".app-inline-note-info").first(), { expectWrapped: true });
        await addAlertFixtures(page);

        for (const name of ["inline-info", "inline-warning", "inline-danger", "inline-success", "bootstrap-warning"]) {
            await assertPseudoIconAlertLayout(page.locator(`[data-alert-fixture="${name}"]`), { expectWrapped: true });
        }
        await assertRealIconAlertLayout(page.locator('[data-alert-fixture="notify-success"]'), { expectWrapped: true });
    }
});

test("login error notification keeps icon separate from RTL text", async ({ page }) => {
    await page.setViewportSize({ width: 420, height: 760 });
    await setTheme(page, "dark");
    await ensureOwnerSignedIn(page);
    await signOut(page);

    await page.locator('[name="username"]').fill(owner.username);
    await page.locator('[name="password"]').fill("WrongPassword-123!");
    await page.getByRole("button", { name: "دخول" }).click();

    const notification = page.locator(".app-notify-danger").first();
    await expect(notification).toBeVisible();
    await assertRealIconAlertLayout(notification);
});
