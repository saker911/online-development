const escapeHtml = (value) => {
    const s = String(value ?? '');
    return s.replace(/[&<>"']/g, function (c) {
        return ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        })[c];
    });
};

document.addEventListener("DOMContentLoaded", function () {
    const form = document.getElementById("scanConsoleForm");
    const input = document.querySelector(".scan-input");
    const detectedScanModeText = document.getElementById("detectedScanModeText");
    const scannedPermitCard = document.getElementById("scannedPermitCard");
    const scannedPermitPublicCode = document.getElementById("scannedPermitPublicCode");
    const scannedPermitDetails = document.getElementById("scannedPermitDetails");
    const cameraButton = document.getElementById("scanConsoleCameraButton");
    const cameraPanel = document.getElementById("scanConsoleCameraPanel");
    const cameraPreview = document.getElementById("scanConsoleCameraPreview");
    const cameraStatus = document.getElementById("scanConsoleCameraStatus");
    const stopCameraButton = document.getElementById("scanConsoleStopCameraButton");
    const fullscreenButton = document.getElementById("scanConsoleFullscreenButton");
    const antiforgeryTokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    const scannerBuffer = [];
    let scannerTimer = null;
    let autoSubmitTimer = null;
    let isSubmitting = false;
    let cameraStream = null;
    let cameraFrameRequest = 0;
    let cameraDetector = null;
    let cameraRunning = false;
    let cameraScanLocked = false;
    let pendingExecutionMethod = "manual";
    const scannerDeviceId = resolveScannerDeviceId();
    const topRowShiftedDigits = {
        Digit1: '!',
        Digit2: '@',
        Digit3: '#',
        Digit4: '$',
        Digit5: '%',
        Digit6: '^',
        Digit7: '&',
        Digit8: '*',
        Digit9: '(',
        Digit0: ')'
    };
    const punctuationByCode = {
        Minus: ['-', '_'],
        Equal: ['=', '+'],
        BracketLeft: ['[', '{'],
        BracketRight: [']', '}'],
        Backslash: ['\\', '|'],
        Semicolon: [';', ':'],
        Quote: ['\'', '"'],
        Backquote: ['`', '~'],
        Comma: [',', '<'],
        Period: ['.', '>'],
        Slash: ['/', '?'],
        Space: [' ', ' '],
        NumpadDivide: ['/', '/'],
        NumpadMultiply: ['*', '*'],
        NumpadSubtract: ['-', '-'],
        NumpadAdd: ['+', '+'],
        NumpadDecimal: ['.', '.']
    };

    function resolveScannerDeviceId() {
        const storageKey = "vps_scan_device_id";
        try {
            const stored = window.localStorage.getItem(storageKey);
            if (stored) {
                return stored;
            }

            const generated = `browser-${window.crypto?.randomUUID?.() || Date.now().toString(36)}`;
            window.localStorage.setItem(storageKey, generated);
            return generated;
        }
        catch {
            return `browser-session-${Date.now().toString(36)}`;
        }
    }

    const translations = {
        "Entry recorded": "تم السماح بالدخول بهذا التصريح.",
        "OverrideEntry recorded": "تم السماح بالدخول استثنائياً.",
        "Out recorded": "تم تسجيل خروج التصريح بنجاح.",
        "Return recorded": "تمت عودة هذا التصريح بنجاح.",
        "LeaveWindowNotStarted": "لم تبدأ فترة الاستئذان بعد، ولا يمكن الخروج قبل وقت البداية المحدد.",
        "LateReturn recorded": "تم تسجيل عودة متأخرة لهذا التصريح.",
        "ExitAuthorized recorded": "تم تسجيل خروج التصريح بإذن.",
        "ExitFinal recorded": "تم تسجيل خروج نهائي لهذا التصريح.",
        "ReturnAfterUnauthorizedExit recorded": "تم تسجيل عودة بعد خروج غير مصرح مع حفظ المخالفة.",
        "ExitUnauthorized recorded": "تم تسجيل خروج التصريح بدون إذن.",
        "WorkEndEntry recorded": "تم تسجيل دخول التصريح بعد إغلاق نهاية الدوام.",
        "PendingUnauthorizedExit": "تم رفض الخروج، وسجلت الحالة كمحاولة معلقة للمراجعة.",
        "UnauthorizedExitNeedsReview": "الحالة تحولت إلى مراجعة إدارية بعد نهاية اليوم.",
        "UnauthorizedExitStopped": "تم إيقاف التصريح بسبب تكرار الخروج بدون استئذان.",
        "DeniedAttemptClosed": "أغلقت المحاولة المعلقة دون تسجيل مخالفة نهائية.",
        "AlreadyEntered": "هذا التصريح تم إدخاله مسبقًا خلال وقت قصير.",
        "AlreadyExited": "هذا التصريح تم خروجه مسبقًا خلال وقت قصير.",
        "NoReturnWarning recorded": "تم إرسال تنبيه بتأخر العودة.",
        "DuplicateIgnored": "تم تجاهل القراءة لأنها مكررة.",
        "SecurityViolation": "تم إيقاف التصريح بسبب مخالفة أمنية.",
        "NoReturnViolation": "تم إيقاف التصريح لعدم العودة في الوقت المحدد.",
        "No return required": "تم تسجيل خروج هذا التصريح بدون عودة، ولا يلزم تمريره مرة أخرى.",
        "Permit expired": "التصريح منتهي ولا يمكن استخدامه.",
        "permit_not_active": "هذا التصريح غير نشط حاليًا أو ما زال بانتظار الاعتماد.",
        "Permit not found": form?.dataset.permitNotFoundMessage || "رقم التصريح غير موجود.",
        "Already returned": "تمت عودة هذا التصريح مسبقًا.",
        "visit_not_found": "رقم الزيارة غير موجود.",
        "visit_expired": "الزيارة منتهية.",
        "visit_not_approved": "الزيارة ما زالت بانتظار الاعتماد أو تم رفضها.",
        "entry_recorded": "تم تسجيل دخول الزيارة بنجاح.",
        "exit_recorded": "تم تسجيل خروج الزيارة بنجاح.",
        "invalid_request": "تعذر تنفيذ القراءة. تأكد من القيمة المقروءة ثم أعد المحاولة."
    };

    function resetScannerBuffer() {
        scannerBuffer.length = 0;
        if (scannerTimer) {
            window.clearTimeout(scannerTimer);
            scannerTimer = null;
        }
        if (autoSubmitTimer) {
            window.clearTimeout(autoSubmitTimer);
            autoSubmitTimer = null;
        }
    }

    function armScannerFocus() {
        if (!input || cameraRunning) {
            return;
        }

        input.focus({ preventScroll: true });
        input.select();
    }

    function getEnglishCharacterFromEvent(event) {
        const code = String(event.code || '');
        if (!code) {
            return '';
        }

        if (code.startsWith('Key') && code.length === 4) {
            const letter = code.slice(3);
            return event.shiftKey ? letter.toUpperCase() : letter.toLowerCase();
        }

        if (code.startsWith('Digit') && code.length === 6) {
            return event.shiftKey ? (topRowShiftedDigits[code] || '') : code.slice(5);
        }

        if (code.startsWith('Numpad') && code.length === 7) {
            const digit = code.slice(6);
            return /^\d$/.test(digit) ? digit : '';
        }

        if (Object.prototype.hasOwnProperty.call(punctuationByCode, code)) {
            const values = punctuationByCode[code];
            return event.shiftKey ? values[1] : values[0];
        }

        return '';
    }

    function playToneSequence(type) {
        const AudioContextClass = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextClass) {
            return;
        }

        const audioContext = new AudioContextClass();
        const gain = audioContext.createGain();
        gain.gain.value = type === "success" ? 0.42 : 0.48;
        gain.connect(audioContext.destination);

        const now = audioContext.currentTime;
        const notes = type === "success" ? [880, 1174, 1480] : [220, 180, 140];

        notes.forEach(function (frequency, index) {
            const oscillator = audioContext.createOscillator();
            oscillator.type = type === "success" ? "triangle" : "sawtooth";
            oscillator.frequency.value = frequency;
            oscillator.connect(gain);

            const startAt = now + (index * 0.12);
            const duration = type === "success" ? 0.11 : 0.14;
            oscillator.start(startAt);
            oscillator.stop(startAt + duration);
        });

        window.setTimeout(function () {
            audioContext.close().catch(function () { });
        }, 900);
    }

    function translateReason(reason, currentMode) {
        if (reason === "not_allowed") {
            return currentMode === "visit"
                ? "لا يمكن تنفيذ هذه العملية على الزيارة الحالية."
                : "لا يمكن تنفيذ هذه العملية على التصريح الحالي.";
        }

        return translations[reason] || reason || "تمت المعالجة بدون تفاصيل إضافية.";
    }

    function buildSuccessMessage(payload, currentMode) {
        const displayName = (payload.displayName || "").trim() || "الشخص";
        switch (payload.reason) {
            case "Entry recorded":
            case "entry_recorded":
                return `تم دخول ${displayName}.`;
            case "OverrideEntry recorded":
                return `تم السماح بدخول ${displayName} استثنائياً.`;
            case "Out recorded":
                return `تم خروج ${displayName}.`;
            case "Return recorded":
                return `تمت عودة ${displayName}.`;
            case "LateReturn recorded":
                return `تمت عودة ${displayName} متأخرًا.`;
            case "ExitAuthorized recorded":
                return `تم خروج ${displayName} بإذن.`;
            case "ExitFinal recorded":
                return `تم خروج ${displayName} نهائيًا.`;
            case "ReturnAfterUnauthorizedExit recorded":
                return `تمت عودة ${displayName} بعد خروج غير مصرح، وتم توثيق الحالة.`;
            case "ExitUnauthorized recorded":
                return `تم خروج ${displayName} بدون إذن.`;
            case "WorkEndEntry recorded":
                return `تم تسجيل دخول ${displayName} بعد إغلاق نهاية الدوام.`;
            case "PendingUnauthorizedExit":
                return `تم رفض خروج ${displayName} وسجلت الحالة كمحاولة معلقة.`;
            case "UnauthorizedExitNeedsReview":
                return `تم تحويل حالة ${displayName} إلى مراجعة إدارية.`;
            case "UnauthorizedExitStopped":
                return `تم إيقاف تصريح ${displayName} بسبب تكرار الخروج بدون استئذان.`;
            case "DeniedAttemptClosed":
                return `أغلقت محاولة ${displayName} دون مخالفة نهائية.`;
            case "AlreadyEntered":
                return `تم إدخال ${displayName} مسبقًا.`;
            case "AlreadyExited":
                return `تم إخراج ${displayName} مسبقًا.`;
            case "NoReturnWarning recorded":
                return `تم إرسال تنبيه بتأخر عودة ${displayName}.`;
            case "DuplicateIgnored":
                return `تم تجاهل القراءة المكررة.`;
            case "exit_recorded":
                return `تم خروج ${displayName}.`;
            default:
                return translateReason(payload.reason, currentMode);
        }
    }

    function renderResult(isAllowed, message, identifier) {
        const formattedMessage = identifier
            ? `${message} (آخر قيمة مقروءة: ${identifier})`
            : message;
        if (window.appToast) {
            if (isAllowed) {
                window.appToast.success(formattedMessage);
            }
            else {
                window.appToast.error(formattedMessage);
            }
        }
        playToneSequence(isAllowed ? "success" : "error");
        if (navigator.vibrate) {
            navigator.vibrate(isAllowed ? [90] : [180, 80, 180]);
        }
    }

    function renderPermitDetails(permit) {
        if (!scannedPermitCard) {
            return;
        }

        if (!permit) {
            scannedPermitCard.classList.add("d-none");
            if (scannedPermitPublicCode) {
                scannedPermitPublicCode.textContent = "";
            }
            if (scannedPermitDetails) {
                scannedPermitDetails.classList.add("d-none");
                scannedPermitDetails.innerHTML = "";
            }
            return;
        }

        scannedPermitCard.classList.remove("d-none");
        if (scannedPermitPublicCode) {
            scannedPermitPublicCode.textContent = permit.publicPermitCode || "";
        }

        if (scannedPermitDetails) {
            scannedPermitDetails.classList.remove("d-none");
            scannedPermitDetails.innerHTML = `
                <div class="col-md-6">
                    <strong>رقم التصريح</strong>
                    <div class="ltr-field">${escapeHtml(permit.permitNumber)}</div>
                </div>
                <div class="col-md-6">
                    <strong>اسم المصرح له</strong>
                    <div>${escapeHtml(permit.driverName)}</div>
                </div>
                <div class="col-md-6">
                    <strong>الحالة الحالية</strong>
                    <div>${escapeHtml(permit.approvalStatusDisplay)}</div>
                </div>
                <div class="col-md-6">
                    <strong>نوع التصريح</strong>
                    <div>${escapeHtml(permit.permitTypeDisplay)}</div>
                </div>
                <div class="col-md-6">
                    <strong>القسم</strong>
                    <div>${escapeHtml(permit.departmentName)}</div>
                </div>
                <div class="col-md-6">
                    <strong>موقع العمل / الزيارة</strong>
                    <div>${escapeHtml(permit.locationDisplay)}</div>
                </div>
                <div class="col-md-6">
                    <strong>المعرف الداخلي</strong>
                    <div class="national-id-field">${escapeHtml(permit.nationalId)}</div>
                </div>
                <div class="col-md-6">
                    <strong>رقم اللوحة</strong>
                    <div class="ltr-field">${escapeHtml(permit.plateNumberDisplay)}</div>
                </div>
                <div class="col-md-6">
                    <strong>نوع المركبة</strong>
                    <div>${escapeHtml(permit.vehicleType)}</div>
                </div>
                <div class="col-md-6">
                    <strong>رقم الجوال</strong>
                    <div class="ltr-field">${escapeHtml(permit.employeePhone)}</div>
                </div>
                <div class="col-12">
                    <strong>نص التصريح</strong>
                    <div>${escapeHtml(permit.subject)}</div>
                </div>`;
        }
    }

    function detectScanMode(rawIdentifier) {
        const normalized = (rawIdentifier || "").trim().toUpperCase();

        if (!normalized) {
            return "permit";
        }

        if (normalized.startsWith("PERMIT")) {
            return "permit";
        }

        if (/^V\d+/.test(normalized) || normalized.includes("VISIT")) {
            return "visit";
        }

        return "permit";
    }

    function updateDetectedModeLabel(currentMode) {
        if (!detectedScanModeText) {
            return;
        }

        detectedScanModeText.textContent = currentMode === "visit"
            ? "تم اكتشاف القيمة كزيارة تلقائيًا."
            : "تم اكتشاف القيمة كتـصريح تلقائيًا.";
    }

    async function readJsonResponse(response) {
        try {
            return await response.json();
        }
        catch {
            return null;
        }
    }

    async function submitScan(forceOverride) {
        if (isSubmitting) {
            return;
        }

        const rawIdentifier = (input.value || "").trim();
        const currentMode = detectScanMode(rawIdentifier);
        const identifier = currentMode === "permit"
            ? rawIdentifier.toUpperCase()
            : rawIdentifier;

        if (!identifier) {
            return;
        }

        isSubmitting = true;
        updateDetectedModeLabel(currentMode);

        if (forceOverride && currentMode !== "permit") {
            renderResult(false, "السماح الاستثنائي متاح للتصاريح فقط.", identifier);
            isSubmitting = false;
            return;
        }

        try {
            const response = await fetch(form.dataset.scanEndpoint, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": antiforgeryTokenInput ? antiforgeryTokenInput.value : ""
                },
                body: JSON.stringify({
                    identifier: identifier,
                    scannerUserId: form.dataset.scannerUser || "",
                    overrideEntry: !!forceOverride,
                    gateName: window.matchMedia("(max-width: 767.98px)").matches
                        ? "ماسح الجوال"
                        : "وحدة المسح",
                    executionMethod: pendingExecutionMethod,
                    deviceId: scannerDeviceId
                })
            });

            if (response.status === 401 || response.status === 403) {
                renderResult(false, translations.forbidden, identifier);
                isSubmitting = false;
                return;
            }

            if (!response.ok) {
                const errorPayload = await readJsonResponse(response);
                const errorMessage = errorPayload?.reason
                    ? translateReason(errorPayload.reason, currentMode)
                    : translations.invalid_request;

                if (errorPayload?.scanMode) {
                    updateDetectedModeLabel(errorPayload.scanMode);
                }

                if (errorPayload?.permit) {
                    renderPermitDetails(errorPayload.permit);
                }

                renderResult(false, errorMessage, errorPayload?.identifier || identifier);
                isSubmitting = false;
                return;
            }

            const payload = await readJsonResponse(response);
            if (!payload) {
                renderResult(false, translations.invalid_request, identifier);
                renderPermitDetails(null);
                return;
            }
            const payloadMode = payload.scanMode || currentMode;
            const message = payload.allowed
                ? buildSuccessMessage(payload, payloadMode)
                : translateReason(payload.reason, payloadMode);

            updateDetectedModeLabel(payloadMode);
            renderResult(!!payload.allowed, message, payload.identifier || identifier);
            renderPermitDetails(payload.permit || null);
            input.value = "";
            if (!cameraRunning) {
                input.focus();
            }
        }
        catch {
            renderResult(false, translations.invalid_request, identifier);
            renderPermitDetails(null);
        }
        finally {
            isSubmitting = false;
        }
    }

    function setCameraStatus(message, isError) {
        if (!cameraStatus) {
            return;
        }

        cameraStatus.textContent = message;
        cameraStatus.classList.toggle("is-error", !!isError);
    }

    function stopCameraScanner(message) {
        cameraRunning = false;
        cameraScanLocked = false;
        if (cameraFrameRequest) {
            window.cancelAnimationFrame(cameraFrameRequest);
            cameraFrameRequest = 0;
        }

        if (cameraStream) {
            cameraStream.getTracks().forEach(function (track) {
                track.stop();
            });
            cameraStream = null;
        }

        if (cameraPreview) {
            cameraPreview.srcObject = null;
        }

        if (cameraPanel) {
            cameraPanel.hidden = true;
        }

        if (cameraButton) {
            cameraButton.disabled = false;
        }

        if (isCameraFullscreen()) {
            exitCameraFullscreen();
        }

        if (message) {
            setCameraStatus(message, false);
        }

        if (input) {
            if (!cameraRunning) {
                input.focus();
            }
        }
    }

    async function detectCameraFrame() {
        if (!cameraRunning || cameraScanLocked || !cameraDetector || !cameraPreview) {
            return;
        }

        try {
            const codes = await cameraDetector.detect(cameraPreview);
            const rawValue = codes && codes.length ? (codes[0].rawValue || "").trim() : "";
            if (rawValue) {
                cameraScanLocked = true;
                input.value = rawValue;
                resetScannerBuffer();
                pendingExecutionMethod = "camera";
                setCameraStatus("تمت القراءة. جاري التحقق...");
                await submitScan();
                if (cameraRunning) {
                    setCameraStatus("جاهز للرمز التالي.");
                    window.setTimeout(function () {
                        cameraScanLocked = false;
                        if (cameraRunning) {
                            cameraFrameRequest = window.requestAnimationFrame(detectCameraFrame);
                        }
                    }, 1100);
                }
                return;
            }
        }
        catch {
            setCameraStatus("تعذر تحليل الصورة. قرب الكاميرا من الرمز أو استخدم قارئ الباركود.", true);
        }

        cameraFrameRequest = window.requestAnimationFrame(detectCameraFrame);
    }

    function isCameraFullscreen() {
        return document.fullscreenElement === cameraPanel
            || document.webkitFullscreenElement === cameraPanel;
    }

    function requestCameraFullscreen() {
        if (!cameraPanel || isCameraFullscreen()) {
            return;
        }

        const request = cameraPanel.requestFullscreen || cameraPanel.webkitRequestFullscreen;
        if (request) {
            Promise.resolve(request.call(cameraPanel)).catch(function () { });
        }
    }

    function exitCameraFullscreen() {
        const exit = document.exitFullscreen || document.webkitExitFullscreen;
        if (exit) {
            Promise.resolve(exit.call(document)).catch(function () { });
        }
    }

    async function startCameraScanner() {
        if (!cameraButton || !cameraPanel || !cameraPreview) {
            return;
        }

        if (!window.isSecureContext && !["localhost", "127.0.0.1"].includes(window.location.hostname)) {
            cameraPanel.hidden = false;
            setCameraStatus("قراءة الكاميرا على الجوال تحتاج تشغيل الموقع عبر HTTPS.", true);
            return;
        }

        if (!("BarcodeDetector" in window) || !navigator.mediaDevices?.getUserMedia) {
            cameraPanel.hidden = false;
            setCameraStatus("الكاميرا غير مدعومة في هذا المتصفح. استخدم Chrome/Edge أو قارئ الباركود الخارجي.", true);
            return;
        }

        try {
            cameraButton.disabled = true;
            cameraPanel.hidden = false;
            setCameraStatus("جاري تشغيل كاميرا الجوال...");
            if (window.matchMedia("(max-width: 767.98px)").matches) {
                requestCameraFullscreen();
            }

            const formats = ["qr_code", "code_128", "code_39", "ean_13", "ean_8", "itf", "upc_a", "upc_e"];
            const supportedFormats = typeof BarcodeDetector.getSupportedFormats === "function"
                ? await BarcodeDetector.getSupportedFormats()
                : formats;
            const activeFormats = formats.filter(function (format) {
                return supportedFormats.includes(format);
            });

            cameraDetector = new BarcodeDetector(activeFormats.length ? { formats: activeFormats } : undefined);
            cameraStream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: { ideal: "environment" } },
                audio: false
            });
            cameraPreview.srcObject = cameraStream;
            await cameraPreview.play();
            cameraRunning = true;
            setCameraStatus("وجّه كاميرا الجوال نحو QR أو الباركود.");
            cameraFrameRequest = window.requestAnimationFrame(detectCameraFrame);
        }
        catch {
            stopCameraScanner();
            if (cameraPanel) {
                cameraPanel.hidden = false;
            }
            setCameraStatus("تعذر تشغيل الكاميرا. تحقق من السماح للمتصفح باستخدام الكاميرا.", true);
        }
    }

    function queueAutoSubmit() {
        if (autoSubmitTimer) {
            window.clearTimeout(autoSubmitTimer);
        }

        autoSubmitTimer = window.setTimeout(function () {
            autoSubmitTimer = null;
            if (input && input.value.trim()) {
                pendingExecutionMethod = "barcode_scanner";
                resetScannerBuffer();
                submitScan();
            }
        }, 250);
    }

    document.addEventListener("keydown", function (event) {
        if (event.defaultPrevented) {
            return;
        }

        if (event.key === "Enter") {
            event.preventDefault();
            event.stopPropagation();
            pendingExecutionMethod = scannerBuffer.length ? "barcode_scanner" : "manual";
            resetScannerBuffer();
            submitScan();
            return;
        }

        if (!event.ctrlKey && !event.metaKey && !event.altKey) {
            const englishCharacter = getEnglishCharacterFromEvent(event);
            if (!englishCharacter) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            scannerBuffer.push(englishCharacter);
            if (input) {
                input.value = scannerBuffer.join("");
            }

            if (scannerTimer) {
                window.clearTimeout(scannerTimer);
            }

            scannerTimer = window.setTimeout(resetScannerBuffer, 500);
            queueAutoSubmit();
        }
    }, true);

    if (input) {
        armScannerFocus();
        window.requestAnimationFrame(armScannerFocus);
        window.setTimeout(armScannerFocus, 0);
    }

    window.addEventListener("load", armScannerFocus);
    window.addEventListener("focus", armScannerFocus);
    document.addEventListener("visibilitychange", function () {
        if (!document.hidden) {
            armScannerFocus();
        }
    });
    document.addEventListener("click", function (event) {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        if (target.closest("select, button, a, textarea, input, label, option")) {
            return;
        }

        if (input && document.activeElement !== input) {
            armScannerFocus();
        }
    });

    if (form) {
        form.addEventListener("submit", function (event) {
            event.preventDefault();
            pendingExecutionMethod = "manual";
            resetScannerBuffer();
            submitScan();
        });
    }

    if (cameraButton) {
        cameraButton.addEventListener("click", startCameraScanner);
    }

    if (fullscreenButton) {
        fullscreenButton.addEventListener("click", function () {
            if (isCameraFullscreen()) {
                exitCameraFullscreen();
            }
            else {
                requestCameraFullscreen();
            }
        });
    }

    if (stopCameraButton) {
        stopCameraButton.addEventListener("click", function () {
            stopCameraScanner("تم إيقاف الكاميرا.");
        });
    }

    window.addEventListener("pagehide", function () {
        stopCameraScanner();
    });

    const initialPermitScript = document.getElementById("scanConsoleInitialPermit");
    if (initialPermitScript) {
        try {
            const initialPermit = JSON.parse(initialPermitScript.textContent || "null");
            if (initialPermit) {
                renderPermitDetails(initialPermit);
            }
        }
        catch {
        }
    }
});
