(() => {
  if (!("serviceWorker" in navigator) || !window.isSecureContext) {
    return;
  }

  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/service-worker.js").catch(() => {});
  });

  let installPrompt = null;
  const installButton = document.querySelector("[data-pwa-install]");

  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    installPrompt = event;
    installButton?.removeAttribute("hidden");
  });

  installButton?.addEventListener("click", async () => {
    if (!installPrompt) {
      return;
    }

    installButton.setAttribute("hidden", "");
    installPrompt.prompt();
    await installPrompt.userChoice;
    installPrompt = null;
  });

  window.addEventListener("appinstalled", () => {
    installPrompt = null;
    installButton?.setAttribute("hidden", "");
  });
})();
