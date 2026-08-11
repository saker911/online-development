(() => {
  const status = document.querySelector("[data-connection-status]");
  const retryLink = document.querySelector("[data-retry-link]");
  let checking = false;

  const restoreApplication = () => {
    if (status) {
      status.textContent = "عاد الاتصال. جاري فتح النظام...";
    }

    window.location.replace(`/?reconnected=${Date.now()}`);
  };

  const checkConnection = async () => {
    if (checking) {
      return;
    }

    checking = true;
    try {
      const response = await fetch(`/healthz?offline-check=${Date.now()}`, {
        cache: "no-store",
        credentials: "same-origin",
        headers: { Accept: "application/json" }
      });

      if (response.ok) {
        restoreApplication();
        return;
      }
    } catch {
      // Keep the offline screen visible until the next scheduled check.
    } finally {
      checking = false;
    }

    if (status) {
      status.textContent = "لم يعد الاتصال بعد. سنحاول مجدداً تلقائياً.";
    }
  };

  retryLink?.addEventListener("click", (event) => {
    event.preventDefault();
    if (status) {
      status.textContent = "جاري التحقق من الاتصال...";
    }
    checkConnection();
  });

  window.addEventListener("online", checkConnection);
  window.setInterval(checkConnection, 5000);
  checkConnection();
})();
