const { expect, test } = require("@playwright/test");
const {
  createTestPngBuffer,
  ensureOwnerSignedIn,
  ensureOperationalAdminSignedIn,
} = require("./helpers/e2e-helpers");

test("workplace navigation exposes activity people attendance and sites", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  await page.goto("/People");
  await expect(page.getByRole("heading", { name: "الأشخاص", exact: true })).toBeVisible();
  const workplaceNavigation = page.getByRole("navigation", { name: "مساحات التشغيل" });
  for (const label of ["النشاط", "الأشخاص", "الحضور", "المواقع"]) {
    await expect(workplaceNavigation.getByRole("link", { name: label, exact: true })).toBeVisible();
  }
  const peopleRows = page
    .getByRole("region", { name: "قائمة الأشخاص" })
    .locator(".workplace-person-row");
  expect(await peopleRows.count()).toBeGreaterThan(0);
  await peopleRows.first().getByRole("link", { name: /فتح ملف/ }).click();
  await expect(page.getByRole("heading", { name: "ملف الشخص" })).toBeVisible();
  await expect(page.getByRole("region", { name: "ملخص ملف الشخص" })).toBeVisible();
  await page.getByRole("link", { name: "العودة إلى الأشخاص" }).click();
  await expect(page.getByRole("heading", { name: "الأشخاص", exact: true })).toBeVisible();

  const visitorRow = page.locator(".workplace-person-row").filter({ hasText: "زائر" }).first();
  if (await visitorRow.count()) {
    const visitorName = (await visitorRow.locator(".workplace-person-link").innerText()).trim();
    await visitorRow.getByRole("link", { name: /فتح ملف/ }).click();
    await page.getByRole("link", { name: "زيارة جديدة" }).click();
    await expect(page.getByLabel("اسم الزائر الرئيسي")).toHaveValue(visitorName);
  }

  await page.goto("/Attendance");
  await expect(page.getByRole("heading", { name: "الحضور", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "ملخص الحضور" })).toBeVisible();

  await page.goto("/Sites");
  await expect(page.getByRole("heading", { name: "المواقع", exact: true })).toBeVisible();
  await expect(page.getByLabel("اسم الموقع", { exact: true })).toBeVisible();
});

test("site management creates and removes an operational site", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/Sites");

  await page.getByLabel("اسم الموقع", { exact: true }).fill("فرع اختبار المتصفح");
  await page.getByLabel("رمز الموقع", { exact: true }).fill("E2E-SITE");
  await page.getByLabel("العنوان", { exact: true }).fill("الرياض");
  await page.getByLabel("نطاق الموقع بالمتر", { exact: true }).fill("220");
  await page.getByRole("button", { name: "إضافة الموقع", exact: true }).click();

  const siteRow = page.locator(".workplace-site-row").filter({ hasText: "فرع اختبار المتصفح" });
  await expect(siteRow).toHaveCount(1);
  await expect(siteRow).toContainText("الرياض");
  await siteRow.getByRole("link", { name: "فتح الموقع", exact: true }).click();
  await expect(page.getByRole("heading", { name: "فرع اختبار المتصفح", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "ملخص الموقع" })).toBeVisible();

  await page.getByLabel("اسم المدخل", { exact: true }).fill("البوابة الشمالية");
  await page.getByLabel("الرمز", { exact: true }).fill("NORTH-A");
  await page.getByLabel("الموقع", { exact: true }).fill("الواجهة الشمالية");
  await page.getByRole("button", { name: "إضافة مدخل", exact: true }).click();
  const entranceRow = page.locator(".workplace-entrance-row").filter({ hasText: "البوابة الشمالية" });
  await expect(entranceRow).toHaveCount(1);
  await expect(entranceRow).toContainText("الواجهة الشمالية");
  await page.screenshot({ path: ".artifacts/site-hub-desktop.png", fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.reload();
  const mobileSiteLayout = await page.evaluate(() => ({
    viewportWidth: window.innerWidth,
    documentWidth: document.documentElement.scrollWidth,
    hubColumns: getComputedStyle(document.querySelector(".workplace-site-hub-grid")).gridTemplateColumns,
  }));
  expect(mobileSiteLayout.documentWidth).toBeLessThanOrEqual(mobileSiteLayout.viewportWidth + 1);
  expect(mobileSiteLayout.hubColumns.split(" ")).toHaveLength(1);
  await page.screenshot({ path: ".artifacts/site-hub-mobile.png", fullPage: true });

  await page.evaluate(() => {
    localStorage.setItem("vps_theme", "dark");
    document.documentElement.dataset.theme = "dark";
  });
  await page.reload();
  const darkSiteColors = await page.evaluate(() => ({
    body: getComputedStyle(document.body).backgroundColor,
    form: getComputedStyle(document.querySelector(".workplace-inline-form")).backgroundColor,
  }));
  expect(darkSiteColors.body).toBe("rgb(13, 13, 13)");
  expect(darkSiteColors.form).toBe("rgb(21, 22, 22)");
  await page.screenshot({ path: ".artifacts/site-hub-mobile-dark.png", fullPage: true });

  await entranceRow.getByRole("button", { name: "حذف", exact: true }).click();

  await page.getByRole("link", { name: "المواقع", exact: true }).first().click();
  const createdSiteRow = page.locator(".workplace-site-row").filter({ hasText: "فرع اختبار المتصفح" });
  await createdSiteRow.getByRole("button", { name: "حذف", exact: true }).click();
  await expect(page.locator(".workplace-site-row").filter({ hasText: "فرع اختبار المتصفح" })).toHaveCount(0);
});

test("site services drive rotating access and emergency operations", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/Sites");

  await page.getByLabel("اسم الموقع", { exact: true }).fill("فرع السلامة التجريبي");
  await page.getByLabel("رمز الموقع", { exact: true }).fill("E2E-SAFETY");
  await page.getByLabel("الخدمة الذاتية", { exact: true }).check();
  await page.getByLabel("البوابة", { exact: true }).check();
  await page.getByRole("button", { name: "إضافة الموقع", exact: true }).click();

  const siteRow = page.locator(".workplace-site-row").filter({ hasText: "فرع السلامة التجريبي" });
  await siteRow.getByRole("link", { name: "فتح الموقع", exact: true }).click();
  const siteId = new URL(page.url()).pathname.split("/").pop();
  await expect(page.getByRole("region", { name: "خدمات الموقع" })).toContainText("الخدمة الذاتية");
  const rotatingQr = page.getByRole("img", { name: "رمز الخدمة الذاتية المتجدد للموقع" });
  await expect(rotatingQr).toBeVisible();
  const qrResponse = await page.request.get(await rotatingQr.getAttribute("src"));
  expect(qrResponse.status()).toBe(200);
  expect(qrResponse.headers()["content-type"]).toContain("image/png");

  await page.goto(`/Emergency?siteId=${siteId}`);
  await expect(page.getByRole("heading", { name: "الطوارئ والإخلاء" })).toBeVisible();
  page.on("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "بدء حالة طوارئ" }).click();
  await expect(page.getByText("جلسة نشطة", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "إنهاء الجلسة" }).click();
  await expect(page.getByRole("heading", { name: "لا توجد حالة طوارئ نشطة" })).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  await page.reload();
  const emergencyLayout = await page.evaluate(() => ({
    viewportWidth: window.innerWidth,
    documentWidth: document.documentElement.scrollWidth,
  }));
  expect(emergencyLayout.documentWidth).toBeLessThanOrEqual(emergencyLayout.viewportWidth + 1);
});

test("authorized operator securely adds and removes a person photo", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/People");
  await page.locator(".workplace-person-row").first().getByRole("link", { name: /فتح ملف/ }).click();

  await page.getByText("إدارة الصورة", { exact: true }).click();
  await page.getByLabel("صورة الشخص").setInputFiles({
    name: "person-photo.png",
    mimeType: "image/png",
    buffer: createTestPngBuffer(31, 140, 106),
  });
  await page.getByRole("button", { name: "رفع الصورة" }).click();

  const photo = page.locator('.workplace-avatar-large img[src*="/People/Photo"]');
  await expect(photo).toBeVisible();
  const photoResponse = await page.request.get(await photo.getAttribute("src"));
  expect(photoResponse.status()).toBe(200);
  expect(photoResponse.headers()["content-type"]).toContain("image/png");

  await page.getByText("إدارة الصورة", { exact: true }).click();
  await page.getByRole("button", { name: "حذف الصورة" }).click();
  await expect(page.locator('.workplace-avatar-large img[src*="/People/Photo"]')).toHaveCount(0);
});

test("workplace pages stay responsive and use neutral black in dark mode", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/People");
  await page.locator(".workplace-person-row").first().getByRole("link", { name: /فتح ملف/ }).click();
  const personLayout = await page.evaluate(() => ({
    viewportWidth: window.innerWidth,
    documentWidth: document.documentElement.scrollWidth,
  }));
  expect(personLayout.documentWidth).toBeLessThanOrEqual(personLayout.viewportWidth + 1);

  await page.goto("/Attendance");

  const lightLayout = await page.evaluate(() => ({
    viewportWidth: window.innerWidth,
    documentWidth: document.documentElement.scrollWidth,
  }));
  expect(lightLayout.documentWidth).toBeLessThanOrEqual(lightLayout.viewportWidth + 1);

  await page.getByRole("button", { name: /الوضع النهاري/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await page.waitForTimeout(400);
  const darkColors = await page.evaluate(() => ({
    body: getComputedStyle(document.body).backgroundColor,
    pageVariable: getComputedStyle(document.documentElement).getPropertyValue("--t-page-bg").trim(),
  }));
  expect(darkColors.body).toBe("rgb(13, 13, 13)");
  expect(darkColors.pageVariable).toBe("#0d0d0d");
});

test("home exposes flat permission-aware application cards in light and dark modes", async ({ page }) => {
  await ensureOperationalAdminSignedIn(page);

  for (const theme of ["light", "dark"]) {
    await page.goto("/");
    await page.evaluate((nextTheme) => {
      localStorage.setItem("vps_theme", nextTheme);
      document.documentElement.dataset.theme = nextTheme;
    }, theme);
    await page.reload();

    const actionStyles = await page.locator(".portal-feature-card.is-featured").evaluateAll((cards) =>
      cards.map((card) => {
        const style = getComputedStyle(card);
        return {
          tag: card.tagName,
          href: card.getAttribute("href"),
          background: style.backgroundColor,
          color: style.color,
          border: style.borderColor,
          shadow: style.boxShadow,
          transform: style.transform,
          radius: parseFloat(style.borderRadius),
        };
      })
    );

    expect(actionStyles.length).toBeGreaterThanOrEqual(2);
    for (const style of actionStyles) {
      expect(style.tag).toBe("A");
      expect(style.href).toBeTruthy();
      expect(style.shadow).toBe("none");
      expect(style.transform).toBe("none");
      expect(style.radius).toBeLessThanOrEqual(8);
    }

    const brandMotion = await page.locator(".app-topbar .app-brand").evaluate((brand) => {
      const mark = brand.querySelector(".app-brand-mark");
      return [brand, mark].map((element) => ({
        animation: getComputedStyle(element).animationName,
        transform: getComputedStyle(element).transform,
      }));
    });
    for (const style of brandMotion) {
      expect(style.animation).toBe("none");
      expect(style.transform).toBe("none");
    }
  }
});

test("application launcher stacks cleanly on mobile", async ({ page }) => {
  await ensureOperationalAdminSignedIn(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");

  const mobileLauncher = await page.evaluate(() => {
    const featuredCards = [...document.querySelectorAll(".portal-feature-card.is-featured")];
    return {
      cardCount: featuredCards.length,
      cardWidths: featuredCards.map((card) => Math.round(card.getBoundingClientRect().width)),
      gridColumns: getComputedStyle(document.querySelector(".portal-feature-grid-primary")).gridTemplateColumns,
      viewportWidth: innerWidth,
      documentWidth: document.documentElement.scrollWidth,
    };
  });

  expect(mobileLauncher.cardCount).toBeGreaterThanOrEqual(2);
  expect(mobileLauncher.gridColumns.split(" ")).toHaveLength(1);
  expect(mobileLauncher.cardWidths.every((width) => width <= 390)).toBe(true);
  expect(mobileLauncher.documentWidth).toBeLessThanOrEqual(mobileLauncher.viewportWidth + 1);
});

test("application shell keeps document scrolling available on desktop and mobile", async ({ page }) => {
  await ensureOperationalAdminSignedIn(page);

  for (const viewport of [
    { width: 1440, height: 900 },
    { width: 390, height: 844 },
  ]) {
    await page.setViewportSize(viewport);
    await page.goto("/");
    await page.evaluate(() => {
      const spacer = document.createElement("div");
      spacer.dataset.scrollRegressionSpacer = "true";
      spacer.style.height = "1200px";
      document.querySelector(".app-main-surface").appendChild(spacer);
    });

    const shellState = await page.evaluate(() => ({
      bodyOverflowY: getComputedStyle(document.body).overflowY,
      shellOverflowY: getComputedStyle(document.querySelector(".app-shell")).overflowY,
      scrollingElement: document.scrollingElement?.tagName,
    }));
    expect(shellState.bodyOverflowY).not.toBe("hidden");
    expect(shellState.shellOverflowY).not.toBe("hidden");
    expect(shellState.scrollingElement).toBe("HTML");

    await page.mouse.wheel(0, 600);
    await expect.poll(() => page.evaluate(() => window.scrollY)).toBeGreaterThan(0);
  }

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/Users");
  const menuButton = page.getByRole("button", { name: "فتح القائمة" });
  await menuButton.click();
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.body).overflowY)).toBe("hidden");
  await menuButton.click();
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.body).overflowY)).not.toBe("hidden");
});

test("display and gate sidebar routes never appear active together", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  await page.goto("/Display/Gate");
  await expect(page.locator('.app-sidebar-link.active', { hasText: "مركز البوابة" })).toHaveCount(1);
  await expect(page.locator('.app-sidebar-link.active', { hasText: "شاشات العرض" })).toHaveCount(0);

  await page.goto("/Administration/DisplaySettings");
  await expect(page.locator('.app-sidebar-link.active', { hasText: "شاشات العرض" })).toHaveCount(1);
  await expect(page.locator('.app-sidebar-link.active', { hasText: "مركز البوابة" })).toHaveCount(0);
});

test("desktop shell uses a compact horizontal portal navigation", async ({ page }) => {
  await ensureOperationalAdminSignedIn(page);
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/");

  const shell = await page.evaluate(() => {
    const workspace = getComputedStyle(document.querySelector(".app-workspace"));
    const sidebar = getComputedStyle(document.querySelector(".app-sidebar"));
    const nav = getComputedStyle(document.querySelector(".portal-primary-nav"));
    const main = document.querySelector(".app-main").getBoundingClientRect();
    return {
      workspaceDisplay: workspace.display,
      sidebarDisplay: sidebar.display,
      navDisplay: nav.display,
      navDirection: nav.flexDirection,
      mainTop: Math.round(main.top),
      activePrimaryLinks: document.querySelectorAll(".portal-primary-link.active").length,
      horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
    };
  });

  expect(shell.workspaceDisplay).toBe("block");
  expect(shell.sidebarDisplay).toBe("none");
  expect(shell.navDisplay).toBe("flex");
  expect(shell.navDirection).toBe("row");
  expect(shell.mainTop).toBeLessThan(100);
  expect(shell.activePrimaryLinks).toBe(1);
  expect(shell.horizontalOverflow).toBe(false);

  await page.getByRole("button", { name: "الإدارة", exact: true }).click();
  await expect(page.locator(".portal-manage-dropdown.show")).toBeVisible();
});
