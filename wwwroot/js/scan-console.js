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
    const scannedSubjectTitle = document.getElementById("scannedSubjectTitle");
    const decisionCard = document.getElementById("scanDecisionCard");
    const decisionMark = document.getElementById("scanDecisionMark");
    const decisionKicker = document.getElementById("scanDecisionKicker");
    const decisionTitle = document.getElementById("scanDecisionTitle");
    const decisionMessage = document.getElementById("scanDecisionMessage");
    const decisionCode = document.getElementById("scanDecisionCode");
    const networkStatus = document.getElementById("scanNetworkStatus");
    const recentActivityList = document.getElementById("scanRecentActivityList");
    const shiftScanCount = document.getElementById("shiftScanCount");
    const shiftAllowedCount = document.getElementById("shiftAllowedCount");
    const shiftDeniedCount = document.getElementById("shiftDeniedCount");
    const resetShiftSummaryButton = document.getElementById("resetShiftSummaryButton");
    const cameraButton = document.getElementById("scanConsoleCameraButton");
    const submitButton = document.getElementById("scanConsoleSubmitButton");
    const cameraPanel = document.getElementById("scanConsoleCameraPanel");
    const cameraPreview = document.getElementById("scanConsoleCameraPreview");
    const cameraStatus = document.getElementById("scanConsoleCameraStatus");
    const stopCameraButton = document.getElementById("scanConsoleStopCameraButton");
    const fullscreenButton = document.getElementById("scanConsoleFullscreenButton");
    const antiforgeryTokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    const unifiedGateRoot = document.getElementById("unifiedGateRoot");
    const unifiedGateFullscreenButton = document.getElementById("unifiedGateFullscreenButton");
    const activeOperatorName = document.getElementById("activeOperatorName");
    const openOperatorModalButton = document.getElementById("openOperatorModalButton");
    const signOutOperatorButton = document.getElementById("signOutOperatorButton");
    const operatorModal = document.getElementById("operatorModal");
    const operatorModalTitle = document.getElementById("operatorModalTitle");
    const operatorModalHint = document.getElementById("operatorModalHint");
    const operatorModalMessage = document.getElementById("operatorModalMessage");
    const operatorLoginSection = document.getElementById("operatorLoginSection");
    const operatorPinChangeSection = document.getElementById("operatorPinChangeSection");
    const operatorBadgeInput = document.getElementById("operatorBadgeInput");
    const operatorPinInput = document.getElementById("operatorPinInput");
    const operatorCurrentPinInput = document.getElementById("operatorCurrentPinInput");
    const operatorNewPinInput = document.getElementById("operatorNewPinInput");
    const operatorConfirmPinInput = document.getElementById("operatorConfirmPinInput");
    const submitOperatorLoginButton = document.getElementById("submitOperatorLoginButton");
    const submitOperatorPinChangeButton = document.getElementById("submitOperatorPinChangeButton");
    const closeOperatorModalButton = document.getElementById("closeOperatorModalButton");
    const cancelOperatorModalButton = document.getElementById("cancelOperatorModalButton");
    const requiresOperator = form?.dataset.requiresOperator === "true";
    const scannerBuffer = [];
    let scannerTimer = null;
    let autoSubmitTimer = null;
    let isSubmitting = false;
    let cameraStream = null;
    let cameraFrameRequest = 0;
    let cameraDetector = null;
    let cameraFallbackControls = null;
    let cameraRunning = false;
    let cameraScanLocked = false;
    let operatorBusy = false;
    let activeOperator = requiresOperator
        ? null
        : {
            username: form?.dataset.scannerUser || "",
            displayName: form?.dataset.scannerDisplayName || form?.dataset.scannerUser || "",
            mustChangePin: false
        };
    let pendingExecutionMethod = "manual";
    const scannerDeviceId = resolveScannerDeviceId();
    const shiftStorageKey = `gate_shift_summary:${form?.dataset.scannerUser || "anonymous"}`;
    let shiftSummary = readShiftSummary();
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
        "operator_not_signed_in": "سجّل دخول مأمور البوابة أولاً.",
        "operator_pin_change_required": "يجب تغيير الرقم السري المؤقت قبل بدء المسح.",
        "forbidden": "لا تملك صلاحية تنفيذ المسح من هذا الجهاز.",
        "invalid_request": "تعذر تنفيذ القراءة. تأكد من القيمة المقروءة ثم أعد المحاولة."
    };

    function readShiftSummary() {
        try {
            const stored = JSON.parse(window.sessionStorage.getItem(shiftStorageKey) || "null");
            return {
                total: Number(stored?.total) || 0,
                allowed: Number(stored?.allowed) || 0,
                denied: Number(stored?.denied) || 0
            };
        }
        catch {
            return { total: 0, allowed: 0, denied: 0 };
        }
    }

    function persistShiftSummary() {
        try {
            window.sessionStorage.setItem(shiftStorageKey, JSON.stringify(shiftSummary));
        }
        catch {
        }
    }

    function renderShiftSummary() {
        if (shiftScanCount) shiftScanCount.textContent = String(shiftSummary.total);
        if (shiftAllowedCount) shiftAllowedCount.textContent = String(shiftSummary.allowed);
        if (shiftDeniedCount) shiftDeniedCount.textContent = String(shiftSummary.denied);
    }

    function recordShiftResult(isAllowed) {
        shiftSummary.total += 1;
        shiftSummary[isAllowed ? "allowed" : "denied"] += 1;
        persistShiftSummary();
        renderShiftSummary();
    }

    function updateNetworkStatus() {
        if (!networkStatus) {
            return;
        }

        const isOnline = navigator.onLine;
        networkStatus.classList.toggle("is-online", isOnline);
        networkStatus.classList.toggle("is-offline", !isOnline);
        networkStatus.lastChild.textContent = isOnline ? " متصل" : " غير متصل";
        if (cameraButton) cameraButton.disabled = !isOnline;
    }

    function isOperatorModalOpen() {
        return !!operatorModal && !operatorModal.classList.contains("is-hidden");
    }

    function updateOperatorUi(session) {
        activeOperator = session || null;
        const scannerReady = !requiresOperator || (!!activeOperator && !activeOperator.mustChangePin);
        if (activeOperatorName) {
            activeOperatorName.textContent = activeOperator?.displayName || "لا يوجد مشغل نشط";
        }
        if (signOutOperatorButton) {
            signOutOperatorButton.disabled = !activeOperator;
        }
        if (openOperatorModalButton) {
            openOperatorModalButton.textContent = activeOperator ? "تبديل المشغل" : "دخول المشغل";
        }
        if (input) {
            input.disabled = !scannerReady;
        }
        if (submitButton) {
            submitButton.disabled = !scannerReady;
        }
        if (cameraButton) {
            cameraButton.disabled = !scannerReady;
        }
    }

    function setOperatorMessage(message, mode) {
        if (!operatorModalMessage) {
            return;
        }
        operatorModalMessage.textContent = message || "";
        operatorModalMessage.className = `gate-unified-operator-message${mode ? ` is-${mode}` : ""}`;
    }

    function openOperatorModal(mode, badgeCode) {
        if (!operatorModal) {
            return;
        }

        const changePin = mode === "pin-change";
        operatorModal.classList.remove("is-hidden");
        operatorModal.setAttribute("aria-hidden", "false");
        operatorModal.dataset.mode = changePin ? "pin-change" : "login";
        operatorLoginSection.hidden = changePin;
        operatorPinChangeSection.hidden = !changePin;
        submitOperatorLoginButton.hidden = changePin;
        submitOperatorPinChangeButton.hidden = !changePin;
        operatorModalTitle.textContent = changePin ? "تغيير الرقم السري" : "دخول مأمور البوابة";
        operatorModalHint.textContent = changePin
            ? "اختر رقمًا جديدًا من 6 أرقام قبل بدء العمل."
            : "امسح بطاقة المشغل أو أدخل الرمز والرقم السري.";
        operatorBadgeInput.value = changePin ? operatorBadgeInput.value : (badgeCode || "");
        operatorPinInput.value = "";
        operatorCurrentPinInput.value = changePin ? operatorPinInput.dataset.currentPin || "" : "";
        operatorNewPinInput.value = "";
        operatorConfirmPinInput.value = "";
        setOperatorMessage("", null);
        window.setTimeout(function () {
            (changePin ? operatorCurrentPinInput : operatorBadgeInput)?.focus();
        }, 0);
    }

    function closeOperatorModal() {
        if (!operatorModal || activeOperator?.mustChangePin) {
            return;
        }
        operatorModal.classList.add("is-hidden");
        operatorModal.setAttribute("aria-hidden", "true");
        armScannerFocus();
    }

    async function refreshAntiforgeryToken() {
        if (!antiforgeryTokenInput) {
            return false;
        }
        try {
            const response = await fetch(window.location.href, {
                headers: { "Accept": "text/html" },
                cache: "no-store"
            });
            if (!response.ok) {
                return false;
            }
            const html = await response.text();
            const snapshot = new DOMParser().parseFromString(html, "text/html");
            const nextToken = snapshot.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
            if (!nextToken) {
                return false;
            }
            antiforgeryTokenInput.value = nextToken;
            return true;
        }
        catch {
            return false;
        }
    }

    async function postOperatorRequest(url, body, allowRetry) {
        if (!url) {
            return { response: null, payload: null };
        }
        const send = async function () {
            const response = await fetch(url, {
                method: "POST",
                headers: {
                    "Accept": "application/json",
                    "Content-Type": "application/json",
                    "RequestVerificationToken": antiforgeryTokenInput?.value || ""
                },
                body: JSON.stringify(body),
                cache: "no-store"
            });
            return { response, payload: await readJsonResponse(response) };
        };

        let result = await send();
        if (
            allowRetry !== false
            && !result.payload
            && [400, 401, 403, 419].includes(result.response?.status)
            && await refreshAntiforgeryToken()
        ) {
            result = await send();
        }
        return result;
    }

    async function refreshOperatorStatus() {
        const endpoint = unifiedGateRoot?.dataset.operatorStatusEndpoint;
        if (!requiresOperator || !endpoint) {
            return;
        }
        try {
            const url = new URL(endpoint, window.location.origin);
            url.searchParams.set("deviceId", scannerDeviceId);
            const response = await fetch(url, { headers: { "Accept": "application/json" }, cache: "no-store" });
            const payload = await readJsonResponse(response);
            updateOperatorUi(payload?.operatorSession || null);
            if (!activeOperator) {
                openOperatorModal("login");
            }
            else if (activeOperator.mustChangePin) {
                openOperatorModal("pin-change");
            }
        }
        catch {
            setOperatorMessage("تعذر التحقق من جلسة المشغل.", "error");
        }
    }

    async function submitOperatorLogin() {
        if (operatorBusy) {
            return;
        }
        const badgeCode = (operatorBadgeInput?.value || "").trim().toUpperCase();
        const pin = (operatorPinInput?.value || "").trim();
        if (!badgeCode || !/^\d{6}$/.test(pin)) {
            setOperatorMessage("أدخل رمز المشغل ورقمًا سريًا من 6 أرقام.", "error");
            return;
        }

        operatorBusy = true;
        submitOperatorLoginButton.disabled = true;
        try {
            const result = await postOperatorRequest(
                unifiedGateRoot?.dataset.switchOperatorEndpoint,
                { badgeCode, pin, deviceId: scannerDeviceId }
            );
            if (!result.response?.ok || !result.payload?.success) {
                setOperatorMessage(result.payload?.message || "تعذر تسجيل دخول المشغل.", "error");
                return;
            }

            updateOperatorUi(result.payload.currentOperator);
            if (result.payload.requiresPinChange || activeOperator?.mustChangePin) {
                operatorPinInput.dataset.currentPin = pin;
                openOperatorModal("pin-change");
                return;
            }

            setOperatorMessage("تم تسجيل دخول المشغل.", "success");
            operatorModal.classList.add("is-hidden");
            operatorModal.setAttribute("aria-hidden", "true");
            armScannerFocus();
        }
        finally {
            operatorBusy = false;
            submitOperatorLoginButton.disabled = false;
        }
    }

    async function submitOperatorPinChange() {
        if (operatorBusy) {
            return;
        }
        const currentPin = (operatorCurrentPinInput?.value || "").trim();
        const newPin = (operatorNewPinInput?.value || "").trim();
        const confirmPin = (operatorConfirmPinInput?.value || "").trim();
        if (!/^\d{6}$/.test(currentPin) || !/^\d{6}$/.test(newPin) || newPin !== confirmPin) {
            setOperatorMessage("تحقق من الرقم الحالي وتطابق الرقم الجديد المكوّن من 6 أرقام.", "error");
            return;
        }

        operatorBusy = true;
        submitOperatorPinChangeButton.disabled = true;
        try {
            const result = await postOperatorRequest(
                unifiedGateRoot?.dataset.changePinEndpoint,
                { currentPin, newPin, deviceId: scannerDeviceId }
            );
            if (!result.response?.ok || !result.payload?.success) {
                setOperatorMessage(result.payload?.message || "تعذر تغيير الرقم السري.", "error");
                return;
            }
            updateOperatorUi(result.payload.operatorSession);
            setOperatorMessage("تم تحديث الرقم السري وأصبحت الشاشة جاهزة.", "success");
            operatorModal.classList.add("is-hidden");
            operatorModal.setAttribute("aria-hidden", "true");
            armScannerFocus();
        }
        finally {
            operatorBusy = false;
            submitOperatorPinChangeButton.disabled = false;
        }
    }

    async function signOutOperator() {
        if (!activeOperator || operatorBusy) {
            return;
        }
        operatorBusy = true;
        signOutOperatorButton.disabled = true;
        try {
            const result = await postOperatorRequest(
                unifiedGateRoot?.dataset.signoutOperatorEndpoint,
                { deviceId: scannerDeviceId }
            );
            if (result.response?.ok && result.payload?.success) {
                updateOperatorUi(null);
                openOperatorModal("login");
            }
        }
        finally {
            operatorBusy = false;
            signOutOperatorButton.disabled = !activeOperator;
        }
    }

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
        if (!input || cameraRunning || isOperatorModalOpen()) {
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

    function resolveDecisionTitle(isAllowed, message) {
        if (!isAllowed) return "تم الرفض";
        if (message.includes("خروج")) return "تم تسجيل الخروج";
        if (message.includes("عودة")) return "تم تسجيل العودة";
        return "دخول مسموح";
    }

    function prependRecentActivity(isAllowed, message, identifier, payload) {
        if (!recentActivityList || !payload) {
            return;
        }

        document.getElementById("scanRecentEmptyState")?.remove();
        const displayName = payload.displayName
            || payload.permit?.driverName
            || payload.visit?.visitorName
            || identifier;
        const item = document.createElement("article");
        item.className = `gate-mobile-activity ${isAllowed ? "is-allowed" : "is-denied"}`;
        item.innerHTML = `
            <div>
                <strong>${escapeHtml(displayName)}</strong>
                <span>${escapeHtml(resolveDecisionTitle(isAllowed, message))}</span>
            </div>
            <time>${escapeHtml(new Date().toLocaleTimeString("ar-SA", { hour: "2-digit", minute: "2-digit" }))}</time>`;
        recentActivityList.prepend(item);

        while (recentActivityList.children.length > 6) {
            recentActivityList.lastElementChild?.remove();
        }
    }

    function renderResult(isAllowed, message, identifier, payload) {
        if (decisionCard) {
            decisionCard.classList.remove("state-idle", "state-success", "state-danger");
            decisionCard.classList.add(isAllowed ? "state-success" : "state-danger");
        }
        if (decisionMark) decisionMark.textContent = isAllowed ? "سماح" : "رفض";
        if (decisionKicker) decisionKicker.textContent = "قرار البوابة";
        if (decisionTitle) decisionTitle.textContent = resolveDecisionTitle(isAllowed, message);
        if (decisionMessage) decisionMessage.textContent = message;
        if (decisionCode) decisionCode.textContent = identifier || "";
        playToneSequence(isAllowed ? "success" : "error");
        if (navigator.vibrate) {
            navigator.vibrate(isAllowed ? [90] : [180, 80, 180]);
        }
        if (payload) {
            recordShiftResult(isAllowed);
            prependRecentActivity(isAllowed, message, identifier, payload);
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
        if (scannedSubjectTitle) {
            scannedSubjectTitle.textContent = "بيانات التصريح";
        }

        if (scannedPermitDetails) {
            scannedPermitDetails.classList.remove("d-none");
            scannedPermitDetails.innerHTML = `
                <div class="gate-mobile-detail">
                    <span>المصرح له</span>
                    <strong>${escapeHtml(permit.driverName || "-")}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>رقم التصريح</span>
                    <strong class="ltr-field">${escapeHtml(permit.permitNumber || "-")}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>نوع التصريح</span>
                    <strong>${escapeHtml(permit.permitTypeDisplay || "-")}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>رقم اللوحة</span>
                    <strong class="ltr-field">${escapeHtml(permit.plateNumberDisplay || "-")}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>القسم</span>
                    <strong>${escapeHtml(permit.departmentName || "-")}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>الموقع</span>
                    <strong>${escapeHtml(permit.locationDisplay || "-")}</strong>
                </div>`;
        }
    }

    function renderVisitDetails(visit) {
        if (!scannedPermitCard || !visit) {
            return;
        }

        scannedPermitCard.classList.remove("d-none");
        if (scannedPermitPublicCode) scannedPermitPublicCode.textContent = visit.visitId || "";
        if (scannedSubjectTitle) scannedSubjectTitle.textContent = "بيانات الزيارة";
        if (scannedPermitDetails) {
            scannedPermitDetails.classList.remove("d-none");
            scannedPermitDetails.innerHTML = `
                <div class="gate-mobile-detail">
                    <span>الزائر</span>
                    <strong>${escapeHtml(visit.visitorName || "-")}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>رقم الزيارة</span>
                    <strong class="ltr-field">${escapeHtml(visit.visitId || "-")}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>المضيف</span>
                    <strong>${escapeHtml(visit.hostName || visit.visitedPersonName || "-")}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>الموقع</span>
                    <strong>${escapeHtml(visit.visitLocation || "-")}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>المرافقون</span>
                    <strong>${escapeHtml(visit.companionCount ?? 0)}</strong>
                </div>
                <div class="gate-mobile-detail">
                    <span>الحالة</span>
                    <strong>${escapeHtml(visit.status || "-")}</strong>
                </div>`;
        }
    }

    function renderScanDetails(payload) {
        if (payload?.visit) {
            renderVisitDetails(payload.visit);
            return;
        }

        renderPermitDetails(payload?.permit || null);
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
        if (requiresOperator && rawIdentifier.toUpperCase().startsWith("GATE-")) {
            input.value = "";
            openOperatorModal("login", rawIdentifier.toUpperCase());
            return;
        }
        if (requiresOperator && (!activeOperator || activeOperator.mustChangePin)) {
            openOperatorModal(activeOperator?.mustChangePin ? "pin-change" : "login");
            return;
        }
        const currentMode = detectScanMode(rawIdentifier);
        const isVerificationUrl = /^https?:\/\//i.test(rawIdentifier);
        const identifier = currentMode === "permit" && !isVerificationUrl
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
                    scannerUserId: activeOperator?.username || form.dataset.scannerUser || "",
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

                renderResult(
                    false,
                    errorMessage,
                    errorPayload?.identifier || identifier,
                    errorPayload
                );
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
            renderResult(!!payload.allowed, message, payload.identifier || identifier, payload);
            renderScanDetails(payload);
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

        if (cameraFallbackControls) {
            cameraFallbackControls.stop();
            cameraFallbackControls = null;
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

    async function handleCameraValue(rawValue) {
        const value = String(rawValue || "").trim();
        if (!value || cameraScanLocked) {
            return;
        }

        cameraScanLocked = true;
        input.value = value;
        resetScannerBuffer();
        pendingExecutionMethod = "camera";
        setCameraStatus("تمت القراءة. جاري التحقق...");
        await submitScan();
        if (cameraRunning) {
            setCameraStatus("جاهز للرمز التالي.");
            window.setTimeout(function () {
                cameraScanLocked = false;
                if (cameraRunning && cameraDetector) {
                    cameraFrameRequest = window.requestAnimationFrame(detectCameraFrame);
                }
            }, 1100);
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
                await handleCameraValue(rawValue);
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

        if (!navigator.mediaDevices?.getUserMedia) {
            cameraPanel.hidden = false;
            setCameraStatus("هذا المتصفح لا يسمح بتشغيل الكاميرا. افتح الرابط في Safari أو Chrome واسمح بإذن الكاميرا.", true);
            return;
        }

        try {
            cameraButton.disabled = true;
            cameraPanel.hidden = false;
            setCameraStatus("جاري تشغيل كاميرا الجوال...");
            if (window.matchMedia("(max-width: 767.98px)").matches) {
                requestCameraFullscreen();
            }

            const constraints = {
                video: { facingMode: { ideal: "environment" } },
                audio: false
            };
            cameraRunning = true;

            if ("BarcodeDetector" in window) {
                const formats = ["qr_code", "code_128", "code_39", "ean_13", "ean_8", "itf", "upc_a", "upc_e"];
                const supportedFormats = typeof BarcodeDetector.getSupportedFormats === "function"
                    ? await BarcodeDetector.getSupportedFormats()
                    : formats;
                const activeFormats = formats.filter(function (format) {
                    return supportedFormats.includes(format);
                });

                cameraDetector = new BarcodeDetector(activeFormats.length ? { formats: activeFormats } : undefined);
                cameraStream = await navigator.mediaDevices.getUserMedia(constraints);
                cameraPreview.srcObject = cameraStream;
                await cameraPreview.play();
                setCameraStatus("وجّه كاميرا الجوال نحو QR أو الباركود.");
                cameraFrameRequest = window.requestAnimationFrame(detectCameraFrame);
                return;
            }

            if (!window.ZXingBrowser?.BrowserMultiFormatReader) {
                throw new Error("camera_reader_unavailable");
            }

            cameraDetector = null;
            const fallbackReader = new window.ZXingBrowser.BrowserMultiFormatReader(undefined, {
                delayBetweenScanAttempts: 120,
                delayBetweenScanSuccess: 1100
            });
            cameraFallbackControls = await fallbackReader.decodeFromConstraints(
                constraints,
                cameraPreview,
                function (result) {
                    if (result && cameraRunning && !cameraScanLocked) {
                        handleCameraValue(result.getText ? result.getText() : result.text);
                    }
                }
            );
            if (!cameraRunning) {
                cameraFallbackControls.stop();
                cameraFallbackControls = null;
                return;
            }
            setCameraStatus("وجّه كاميرا الجوال نحو QR أو الباركود.");
        }
        catch (error) {
            stopCameraScanner();
            if (cameraPanel) {
                cameraPanel.hidden = false;
            }
            const permissionDenied = error?.name === "NotAllowedError";
            setCameraStatus(
                permissionDenied
                    ? "تم رفض إذن الكاميرا. اسمح للموقع باستخدامها من إعدادات المتصفح ثم أعد المحاولة."
                    : "تعذر تشغيل الكاميرا. أغلق أي تطبيق يستخدمها ثم أعد المحاولة.",
                true
            );
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

        if (isOperatorModalOpen()) {
            if (event.key === "Escape") {
                event.preventDefault();
                closeOperatorModal();
            }
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
    window.addEventListener("online", updateNetworkStatus);
    window.addEventListener("offline", updateNetworkStatus);
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

    if (resetShiftSummaryButton) {
        resetShiftSummaryButton.addEventListener("click", function () {
            shiftSummary = { total: 0, allowed: 0, denied: 0 };
            persistShiftSummary();
            renderShiftSummary();
        });
    }

    if (openOperatorModalButton) {
        openOperatorModalButton.addEventListener("click", function () {
            openOperatorModal("login");
        });
    }

    if (signOutOperatorButton) {
        signOutOperatorButton.addEventListener("click", signOutOperator);
    }

    if (submitOperatorLoginButton) {
        submitOperatorLoginButton.addEventListener("click", submitOperatorLogin);
    }

    if (submitOperatorPinChangeButton) {
        submitOperatorPinChangeButton.addEventListener("click", submitOperatorPinChange);
    }

    [closeOperatorModalButton, cancelOperatorModalButton].filter(Boolean).forEach(function (button) {
        button.addEventListener("click", closeOperatorModal);
    });

    if (operatorPinInput) {
        operatorPinInput.addEventListener("keydown", function (event) {
            if (event.key === "Enter") {
                event.preventDefault();
                submitOperatorLogin();
            }
        });
    }

    if (operatorConfirmPinInput) {
        operatorConfirmPinInput.addEventListener("keydown", function (event) {
            if (event.key === "Enter") {
                event.preventDefault();
                submitOperatorPinChange();
            }
        });
    }

    if (unifiedGateFullscreenButton) {
        unifiedGateFullscreenButton.addEventListener("click", function () {
            if (document.fullscreenElement) {
                Promise.resolve(document.exitFullscreen?.()).catch(function () { });
            }
            else {
                Promise.resolve(document.documentElement.requestFullscreen?.()).catch(function () { });
            }
        });
        document.addEventListener("fullscreenchange", function () {
            unifiedGateFullscreenButton.textContent = document.fullscreenElement
                ? "إنهاء ملء الشاشة"
                : "ملء الشاشة";
        });
    }

    if (requiresOperator) {
        updateOperatorUi(activeOperator);
        refreshOperatorStatus();
        const heartbeatEndpoint = unifiedGateRoot?.dataset.heartbeatEndpoint;
        if (heartbeatEndpoint) {
            const sendHeartbeat = function () {
                fetch(heartbeatEndpoint, { method: "POST", credentials: "same-origin" }).catch(function () { });
            };
            sendHeartbeat();
            window.setInterval(sendHeartbeat, 30000);
        }
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
    renderShiftSummary();
    updateNetworkStatus();
});
