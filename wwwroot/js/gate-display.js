(() => {
    const root = document.getElementById('permitDisplayRoot');
    if (!root) {
        return;
    }

    const heartbeatEndpoint = root.dataset.heartbeatEndpoint;
    const sendDisplayHeartbeat = () => {
        if (heartbeatEndpoint) {
            fetch(heartbeatEndpoint, { method: 'POST', credentials: 'same-origin' });
        }
    };
    sendDisplayHeartbeat();
    window.setInterval(sendDisplayHeartbeat, 30000);

    const scanEndpoint = root.dataset.scanEndpoint;
    const gateStatusEndpoint = root.dataset.gateStatusEndpoint;
    const operatorStatusEndpoint = root.dataset.operatorStatusEndpoint;
    const switchOperatorEndpoint = root.dataset.switchOperatorEndpoint;
    const signOutOperatorEndpoint = root.dataset.signoutOperatorEndpoint;
    const changePinEndpoint = root.dataset.changePinEndpoint;
    const saveNoteEndpoint = root.dataset.saveNoteEndpoint;
    const scannerInput = document.getElementById('barcodeScanner');
    const focusScannerButton = document.getElementById('focusScannerButton');
    const openGateCameraButton = document.getElementById('openGateCameraButton');
    const gateCameraPanel = document.getElementById('gateCameraPanel');
    const gateCameraPreview = document.getElementById('gateCameraPreview');
    const gateCameraStatus = document.getElementById('gateCameraStatus');
    const stopGateCameraButton = document.getElementById('stopGateCameraButton');
    const antiforgeryTokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    const permitResultCard = document.getElementById('permitResultCard');
    const permitEmptyState = document.getElementById('permitEmptyState');
    const permitDetailsState = document.getElementById('permitDetailsState');
    const scanHintChip = document.getElementById('scanHintChip');
    const permitAccessChip = document.getElementById('permitAccessChip');
    const permitStateChip = document.getElementById('permitStateChip');
    const activeOperatorName = document.getElementById('activeOperatorName');
    const activeOperatorSessionStart = document.getElementById('activeOperatorSessionStart');
    const openOperatorModalButton = document.getElementById('openOperatorModalButton');
    const signOutOperatorButton = document.getElementById('signOutOperatorButton');
    const noteTargetChip = document.getElementById('noteTargetChip');
    const operatorNoteInput = document.getElementById('operatorNoteInput');
    const operatorNoteHint = document.getElementById('operatorNoteHint');
    const saveOperatorNoteButton = document.getElementById('saveOperatorNoteButton');
    const notePresetButtons = Array.from(document.querySelectorAll('[data-note-preset]'));
    const recentActivityList = document.getElementById('recentActivityList');
    const recentActivityTotal = document.getElementById('recentActivityTotal');
    const kioskDeviceLabel = document.getElementById('kioskDeviceLabel');
    const kioskIpLabel = document.getElementById('kioskIpLabel');

    const operatorModal = document.getElementById('operatorModal');
    const operatorModalTitle = document.getElementById('operatorModalTitle');
    const operatorModalHint = document.getElementById('operatorModalHint');
    const operatorModalMessage = document.getElementById('operatorModalMessage');
    const operatorModalCurrentOperator = document.getElementById('operatorModalCurrentOperator');
    const closeOperatorModalButton = document.getElementById('closeOperatorModalButton');
    const cancelOperatorModalButton = document.getElementById('cancelOperatorModalButton');
    const operatorBadgeInput = document.getElementById('operatorBadgeInput');
    const operatorPinInput = document.getElementById('operatorPinInput');
    const operatorLoginSection = document.getElementById('operatorLoginSection');
    const operatorPinChangeSection = document.getElementById('operatorPinChangeSection');
    const operatorCurrentPinInput = document.getElementById('operatorCurrentPinInput');
    const operatorNewPinInput = document.getElementById('operatorNewPinInput');
    const operatorConfirmPinInput = document.getElementById('operatorConfirmPinInput');
    const submitOperatorLoginButton = document.getElementById('submitOperatorLoginButton');
    const submitOperatorPinChangeButton = document.getElementById('submitOperatorPinChangeButton');

    const fields = {
        permitDriverName: document.getElementById('permitDriverName'),
        permitMovementLabel: document.getElementById('permitMovementLabel'),
        permitNumber: document.getElementById('permitNumber'),
        permitType: document.getElementById('permitType'),
        permitDepartment: document.getElementById('permitDepartment'),
        permitApprovalStatus: document.getElementById('permitApprovalStatus'),
        permitPlateNumber: document.getElementById('permitPlateNumber'),
        permitNationalId: document.getElementById('permitNationalId'),
        permitSubject: document.getElementById('permitSubject'),
        permitPhone: document.getElementById('permitPhone'),
        permitReason: document.getElementById('permitReason'),
        permitTimestamp: document.getElementById('permitTimestamp'),
        permitScanSource: document.getElementById('permitScanSource'),
    };

    const deviceId = (() => {
        const storageKey = 'vehicle-permit-gate-device-id';
        const existing = window.localStorage.getItem(storageKey);
        if (existing) {
            return existing;
        }

        const generated = `display-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
        window.localStorage.setItem(storageKey, generated);
        return generated;
    })();

    const RECENT_ACTIVITY_LIMIT = 10;

    const initialRecentActivitiesElement = document.getElementById('gateInitialRecentActivities');
    const initialRecentActivities = (() => {
        try {
            return JSON.parse(initialRecentActivitiesElement?.textContent || '[]');
        } catch {
            return [];
        }
    })();

    let displayedRecentActivities = Array.isArray(initialRecentActivities)
        ? initialRecentActivities.slice(0, RECENT_ACTIVITY_LIMIT)
        : [];

    let audioContext = null;
    let scanBusy = false;
    let scannerTimer = null;
    let autoSubmitTimer = null;
    let scanCooldownUntil = 0;
    let lastSubmittedScanValue = '';
    let lastSubmittedScanAt = 0;
    let activeOperator = null;
    let cameraStream = null;
    let cameraDetector = null;
    let cameraFallbackControls = null;
    let cameraFrameRequest = 0;
    let cameraRunning = false;
    let lastScannedPermitNumber = '';
    let pendingOperatorPin = '';
    let operatorLoginBusy = false;
    let operatorPinChangeBusy = false;
    let operatorSignOutBusy = false;
    let noteSaveBusy = false;
    let statusRefreshHadFailure = false;
    const scannerBuffer = [];
    const operatorBadgeBuffer = [];

    const SCAN_COOLDOWN_DELAY = 1800;
    const REPEATED_SCAN_GUARD_DELAY = 5000;
    const STATUS_REFRESH_INTERVAL = 12000;
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
        Digit0: ')',
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
        NumpadDecimal: ['.', '.'],
    };

    const normalizeReason = (reason) => String(reason ?? '').toLowerCase();
    const escapeHtml = (value) => String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');

    const setChip = (element, text, mode) => {
        element.textContent = text;
        element.className = 'gate-status-chip';
        if (mode) {
            element.classList.add(mode);
        }
    };

    const applyResultTheme = (state) => {
        permitResultCard.classList.remove('state-empty', 'state-success', 'state-danger');
        permitResultCard.classList.add(state);
    };

    const resetResult = (hintText = 'امسح باركود المشغل ثم أدخل الـ PIN') => {
        applyResultTheme('state-empty');
        permitEmptyState.hidden = false;
        permitDetailsState.hidden = true;
        setChip(permitAccessChip, 'غير مقروء', null);
        setChip(permitStateChip, '-', null);
        scanHintChip.textContent = hintText;
        Object.values(fields).forEach((field) => {
            field.textContent = field.id === 'permitScanSource' ? 'قارئ الباركود' : '-';
        });
    };

    const clearPermitContext = (hintText = 'امسح التصريح التالي.') => {
        lastScannedPermitNumber = '';
        operatorNoteInput.value = '';
        noteTargetChip.textContent = 'لا يوجد تصريح محدد';
        saveOperatorNoteButton.disabled = true;
        resetResult(hintText);
        updateOperatorUi(activeOperator);
    };

    const isActiveOperatorReady = () => Boolean(activeOperator && !activeOperator.mustChangePin);

    const updateOperatorUi = (session) => {
        activeOperator = session || null;
        const operatorReady = isActiveOperatorReady();
        activeOperatorName.textContent = session
            ? `${session.displayName}${operatorReady ? '' : ' - تغيير PIN مطلوب'}`
            : 'لا يوجد مشغل نشط';
        activeOperatorSessionStart.textContent = session ? session.signedInAtText || '--:--:--' : '--:--:--';
        openOperatorModalButton.textContent = session
            ? (operatorReady ? 'تبديل مشغل' : 'تغيير PIN')
            : 'دخول مشغل';
        signOutOperatorButton.disabled = !session;
        noteTargetChip.textContent = lastScannedPermitNumber || 'لا يوجد تصريح محدد';
        saveOperatorNoteButton.disabled = !(operatorReady && lastScannedPermitNumber);
        operatorNoteHint.textContent = operatorReady
            ? (lastScannedPermitNumber
                ? `ستُحفظ هذه الملاحظة باسم ${session.displayName} على التصريح ${lastScannedPermitNumber}.`
                : 'يجب عرض تصريح أولاً قبل حفظ الملاحظة.')
            : (session ? 'يجب تغيير PIN المؤقت قبل حفظ أي ملاحظة.' : 'يجب تسجيل دخول مشغل قبل حفظ أي ملاحظة.');
    };

    const renderRecentActivities = (activities) => {
        const nextActivities = Array.isArray(activities)
            ? activities.filter(Boolean).slice(0, RECENT_ACTIVITY_LIMIT)
            : displayedRecentActivities.slice(0, RECENT_ACTIVITY_LIMIT);

        displayedRecentActivities = nextActivities;

        if (!displayedRecentActivities.length) {
            recentActivityList.innerHTML = '<div class="gate-panel-note">لا توجد عمليات حديثة بعد.</div>';
            recentActivityTotal.textContent = '0 عملية';
            return;
        }

        recentActivityList.innerHTML = displayedRecentActivities.map((activity) => {
            const theme = activity.theme || 'danger';
            const title = activity.driverName || activity.permitNumber || activity.actionLabel || '-';
            return `
                <article class="gate-activity-item ${escapeHtml(theme)}">
                    <div class="gate-activity-head">
                        <span>${escapeHtml(activity.occurredAtText || '-')}</span>
                        <span class="gate-activity-badge ${escapeHtml(theme)}">${escapeHtml(activity.statusText || activity.actionLabel || '-')}</span>
                    </div>
                    <div class="gate-activity-title">${escapeHtml(title)}</div>
                    <div class="gate-activity-meta">${escapeHtml(activity.message || '-')}</div>
                    <div class="gate-activity-meta">بواسطة: ${escapeHtml(activity.operatorDisplay || '-')}</div>
                </article>
            `;
        }).join('');

        recentActivityTotal.textContent = `${displayedRecentActivities.length} عملية`;
    };

    const prependRecentActivity = (activity) => {
        if (!activity) {
            return;
        }

        const localActivity = {
            occurredAtText: new Date().toLocaleString('ar-SA', {
                hour: '2-digit',
                minute: '2-digit',
                second: '2-digit',
                day: '2-digit',
                month: '2-digit',
                year: 'numeric',
            }),
            statusText: 'عملية',
            actionLabel: 'عملية',
            driverName: '',
            permitNumber: '',
            message: '',
            operatorDisplay: activeOperator?.displayName || '-',
            theme: 'info',
            ...activity,
        };

        displayedRecentActivities = [
            localActivity,
            ...displayedRecentActivities.filter((item) =>
                !item.id || !localActivity.id || item.id !== localActivity.id
            ),
        ].slice(0, RECENT_ACTIVITY_LIMIT);

        renderRecentActivities(displayedRecentActivities);
    };

    const buildActivitySignature = (activity) => [
        activity.actionLabel || activity.statusText || '',
        activity.permitNumber || '',
        activity.driverName || '',
        activity.message || '',
        activity.operatorDisplay || '',
    ].join('|').toLowerCase();

    const mergeRecentActivities = (serverActivities) => {
        const merged = [];
        const seenIds = new Set();
        const serverSignatures = new Set();
        const localSignatures = new Set();

        [...(Array.isArray(serverActivities) ? serverActivities : []), ...displayedRecentActivities]
            .filter(Boolean)
            .forEach((activity) => {
                const signature = buildActivitySignature(activity);
                if (activity.id) {
                    const id = String(activity.id);
                    if (seenIds.has(id)) {
                        return;
                    }

                    seenIds.add(id);
                    if (signature) {
                        serverSignatures.add(signature);
                    }
                    merged.push(activity);
                    return;
                }

                if (signature && (serverSignatures.has(signature) || localSignatures.has(signature))) {
                    return;
                }

                if (signature) {
                    localSignatures.add(signature);
                }
                merged.push(activity);
            });

        return merged.slice(0, RECENT_ACTIVITY_LIMIT);
    };

    const setModalMessage = (message, mode) => {
        operatorModalMessage.textContent = message;
        operatorModalMessage.className = `gate-dialog-message${mode ? ` ${mode}` : ''}`;
    };

    const isOperatorPinInput = (element) => element === operatorPinInput
        || element === operatorCurrentPinInput
        || element === operatorNewPinInput
        || element === operatorConfirmPinInput;

    const getEnglishCharacterFromEvent = (event) => {
        const code = String(event.code ?? '');
        const key = String(event.key ?? '');
        if (!code) {
            return '';
        }

        if (/^[a-zA-Z]$/.test(key)) {
            return key;
        }

        const shiftedDigitCharacters = {
            Digit1: '!',
            Digit2: '@',
            Digit3: '#',
            Digit4: '$',
            Digit5: '%',
            Digit6: '^',
            Digit7: '&',
            Digit8: '*',
            Digit9: '(',
            Digit0: ')',
        };

        const punctuationCharacters = {
            Minus: ['-', '_'],
            Equal: ['=', '+'],
            BracketLeft: ['[', '{'],
            BracketRight: [']', '}'],
            Backslash: ['\\', '|'],
            Semicolon: [';', ':'],
            Quote: ["'", '"'],
            Comma: [',', '<'],
            Period: ['.', '>'],
            Slash: ['/', '?'],
            Backquote: ['`', '~'],
            Space: [' ', ' '],
        };

        if (code.startsWith('Key') && code.length === 4) {
            const letter = code.slice(3);
            return event.shiftKey ? letter.toUpperCase() : letter.toLowerCase();
        }

        if (code.startsWith('Digit') && code.length === 6) {
            if (/^\d$/.test(key)) {
                return key;
            }

            if (key.length === 1) {
                return key;
            }

            if (event.shiftKey && shiftedDigitCharacters[code]) {
                return shiftedDigitCharacters[code];
            }

            return code.slice(5);
        }

        if (code.startsWith('Numpad') && code.length === 7) {
            const digit = code.slice(6);
            return /^\d$/.test(digit) ? digit : '';
        }

        if (code === 'Minus' || code === 'NumpadSubtract') {
            return '-';
        }

        if (key.length === 1) {
            return key;
        }

        if (code === 'NumpadDecimal') {
            return '.';
        }

        if (code === 'NumpadDivide') {
            return '/';
        }

        if (code === 'NumpadAdd') {
            return '+';
        }

        if (code === 'NumpadMultiply') {
            return '*';
        }

        if (punctuationCharacters[code]) {
            return punctuationCharacters[code][event.shiftKey ? 1 : 0];
        }

        return '';
    };

    const insertInputCharacter = (input, value, maxLength = 0) => {
        if (!input || !value) {
            return;
        }

        const selectionStart = input.selectionStart ?? input.value.length;
        const selectionEnd = input.selectionEnd ?? input.value.length;
        const nextValue = `${input.value.slice(0, selectionStart)}${value}${input.value.slice(selectionEnd)}`;
        input.value = maxLength > 0 ? nextValue.slice(0, maxLength) : nextValue;
        const nextCaret = Math.min(selectionStart + value.length, input.value.length);
        input.setSelectionRange(nextCaret, nextCaret);
    };

    const deletePreviousInputCharacter = (input) => {
        if (!input) {
            return;
        }

        const selectionStart = input.selectionStart ?? input.value.length;
        const selectionEnd = input.selectionEnd ?? input.value.length;

        if (selectionStart !== selectionEnd) {
            input.value = `${input.value.slice(0, selectionStart)}${input.value.slice(selectionEnd)}`;
            input.setSelectionRange(selectionStart, selectionStart);
            return;
        }

        if (selectionStart <= 0) {
            return;
        }

        input.value = `${input.value.slice(0, selectionStart - 1)}${input.value.slice(selectionEnd)}`;
        input.setSelectionRange(selectionStart - 1, selectionStart - 1);
    };

    const getModalTargetInput = () => {
        const activeElement = document.activeElement;
        if (operatorPinChangeSection.hidden) {
            return isOperatorPinInput(activeElement) ? activeElement : operatorBadgeInput;
        }

        return isOperatorPinInput(activeElement) ? activeElement : operatorCurrentPinInput;
    };

    const handleOperatorModalKeydown = (event) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            closeOperatorModal();
            return true;
        }

        if (event.key === 'Tab') {
            return false;
        }

        const targetInput = getModalTargetInput();

        if (event.key === 'Backspace') {
            if (targetInput) {
                event.preventDefault();
                deletePreviousInputCharacter(targetInput);
                return true;
            }

            return false;
        }

        if (event.key === 'Enter') {
            event.preventDefault();

            if (operatorPinChangeSection.hidden) {
                if (targetInput === operatorBadgeInput && operatorBadgeInput.value.trim()) {
                    operatorPinInput.focus({ preventScroll: true });
                    operatorPinInput.select();
                } else {
                    submitOperatorLogin();
                }
            } else {
                submitOperatorPinChange();
            }

            return true;
        }

        if (event.ctrlKey || event.metaKey || event.altKey) {
            return false;
        }

        const englishCharacter = getEnglishCharacterFromEvent(event);
        if (!englishCharacter) {
            return false;
        }

        event.preventDefault();

        if (targetInput === operatorBadgeInput) {
            operatorBadgeInput.focus({ preventScroll: true });
            insertInputCharacter(operatorBadgeInput, englishCharacter.toUpperCase());
            operatorBadgeBuffer.push(englishCharacter.toUpperCase());
            if (operatorBadgeBuffer.length > 64) {
                operatorBadgeBuffer.shift();
            }
            return true;
        }

        if (/^\d$/.test(englishCharacter)) {
            targetInput.focus({ preventScroll: true });
            insertInputCharacter(targetInput, englishCharacter, 6);
            return true;
        }

        return true;
    };

    const isInteractiveTypingTarget = (target) => {
        if (!(target instanceof Element)) {
            return false;
        }

        if (target === scannerInput || scannerInput.contains(target)) {
            return false;
        }

        const interactiveTarget = target.closest('textarea, button, select, [contenteditable="true"], [contenteditable=""], input');
        if (!(interactiveTarget instanceof HTMLElement)) {
            return false;
        }

        if (interactiveTarget === scannerInput) {
            return false;
        }

        if (interactiveTarget instanceof HTMLInputElement) {
            return !interactiveTarget.readOnly && !interactiveTarget.disabled;
        }

        if (interactiveTarget instanceof HTMLTextAreaElement || interactiveTarget instanceof HTMLSelectElement) {
            return !interactiveTarget.disabled;
        }

        if (interactiveTarget instanceof HTMLButtonElement) {
            return !interactiveTarget.disabled;
        }

        return interactiveTarget.isContentEditable;
    };

    const openOperatorModal = (mode = 'login', initialBadge = '') => {
        operatorModal.dataset.mode = mode;
        operatorModal.classList.remove('is-hidden');
        operatorModal.setAttribute('aria-hidden', 'false');
        operatorBadgeBuffer.length = 0;

        const changeMode = mode === 'pin-change';
        operatorBadgeInput.value = changeMode ? operatorBadgeInput.value : (initialBadge || '');
        operatorPinInput.value = '';
        operatorCurrentPinInput.value = pendingOperatorPin;
        operatorNewPinInput.value = '';
        operatorConfirmPinInput.value = '';

        operatorLoginSection.hidden = changeMode;
        operatorPinChangeSection.hidden = !changeMode;
        submitOperatorLoginButton.hidden = changeMode;
        submitOperatorPinChangeButton.hidden = !changeMode;

        if (changeMode) {
            operatorModalTitle.textContent = 'تغيير PIN';
            operatorModalCurrentOperator.textContent = activeOperator ? `الموظف: ${activeOperator.displayName}` : 'الموظف: --';
            operatorModalHint.textContent = 'يجب تغيير الرمز المؤقت قبل متابعة العمل.';
            setModalMessage('أدخل الرمز الحالي ثم اختر رقماً جديداً من 6 أرقام.', null);
            operatorCurrentPinInput.focus({ preventScroll: true });
            operatorCurrentPinInput.select();
        } else {
            operatorModalTitle.textContent = 'إدخال PIN';
            operatorModalCurrentOperator.textContent = activeOperator ? `الموظف الحالي: ${activeOperator.displayName}` : 'الموظف: --';
            operatorModalHint.textContent = activeOperator
                ? 'أدخل PIN للمتابعة وتبديل المشغل الحالي.'
                : 'أدخل PIN للمتابعة.';
            setModalMessage('ملاحظة: سيتم تسجيل خروج المشغل الحالي عند نجاح الدخول.', null);
            operatorBadgeInput.focus({ preventScroll: true });
            operatorBadgeInput.select();
        }
    };

    const closeOperatorModal = () => {
        const mustSignOut = operatorModal.dataset.mode === 'pin-change'
            && activeOperator?.mustChangePin;
        operatorModal.classList.add('is-hidden');
        operatorModal.setAttribute('aria-hidden', 'true');
        operatorBadgeBuffer.length = 0;
        if (mustSignOut) {
            void signOutOperator();
            return;
        }
        focusScanner();
    };

    const buildRequestUrl = (url, query = {}) => {
        const requestUrl = new URL(url, window.location.origin);
        Object.entries(query).forEach(([key, value]) => {
            if (value !== undefined && value !== null && `${value}` !== '') {
                requestUrl.searchParams.set(key, value);
            }
        });

        return requestUrl.toString();
    };

    const buildRequestHeaders = ({ includeJson = false } = {}) => {
        const headers = {
            'Accept': 'application/json',
        };

        if (includeJson) {
            headers['Content-Type'] = 'application/json';
        }

        if (antiforgeryTokenInput?.value) {
            headers['RequestVerificationToken'] = antiforgeryTokenInput.value;
        }

        return headers;
    };

    const refreshAntiforgeryToken = async () => {
        if (!antiforgeryTokenInput) {
            return false;
        }

        try {
            const response = await fetch(window.location.href, {
                headers: { 'Accept': 'text/html' },
                cache: 'no-store',
            });

            if (!response.ok) {
                return false;
            }

            const html = await response.text();
            const documentSnapshot = new DOMParser().parseFromString(html, 'text/html');
            const freshToken = documentSnapshot.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
            if (!freshToken) {
                return false;
            }

            antiforgeryTokenInput.value = freshToken;
            return true;
        } catch {
            return false;
        }
    };

    const shouldRetryWithFreshAntiforgeryToken = (response, payload) => {
        if (payload) {
            return false;
        }

        return response.status === 400
            || response.status === 401
            || response.status === 403
            || response.status === 419;
    };

    const postJson = async (url, body) => {
        const send = async () => {
            const response = await fetch(buildRequestUrl(url), {
                method: 'POST',
                headers: buildRequestHeaders({ includeJson: true }),
                body: JSON.stringify(body),
                cache: 'no-store',
            });

            const payload = await response.json().catch(() => null);
            return { response, payload };
        };

        const result = await send();
        if (shouldRetryWithFreshAntiforgeryToken(result.response, result.payload)) {
            const refreshed = await refreshAntiforgeryToken();
            if (refreshed) {
                return await send();
            }
        }

        return result;
    };

    const playTone = async (type) => {
        try {
            audioContext ??= new (window.AudioContext || window.webkitAudioContext)();
            if (audioContext.state === 'suspended') {
                await audioContext.resume();
            }

            const beep = (frequency, duration, gainValue, when = 0) => {
                const oscillator = audioContext.createOscillator();
                const gainNode = audioContext.createGain();
                oscillator.type = 'sine';
                oscillator.frequency.value = frequency;
                gainNode.gain.value = gainValue;
                oscillator.connect(gainNode);
                gainNode.connect(audioContext.destination);
                oscillator.start(audioContext.currentTime + when);
                oscillator.stop(audioContext.currentTime + when + duration);
            };

            if (type === 'success') {
                beep(880, 0.11, 0.08, 0);
                beep(1175, 0.11, 0.07, 0.14);
            } else if (type === 'warning') {
                beep(420, 0.12, 0.1, 0);
                beep(330, 0.14, 0.09, 0.16);
            } else {
                beep(220, 0.22, 0.11, 0);
            }
        } catch {
            // Ignore audio errors.
        }
    };

    const getFriendlyScanReason = (reason, scanMode, permit, visit) => {
        const reasonKey = normalizeReason(reason);
        const permitType = String(permit?.permitTypeDisplay ?? permit?.permitType ?? '').toLowerCase();
        const isVisitorPermit = permitType.includes('زائر') || permitType.includes('visitor');
        const visitStatus = normalizeReason(visit?.status);

        if (scanMode === 'visit') {
            if (reasonKey.includes('visit_not_approved')) return 'هذه الزيارة غير معتمدة';
            if (reasonKey.includes('visit_expired')) return 'تصريح الزائر منتهي بنهاية الدوام الرسمي';
            if (reasonKey.includes('entry_recorded')) return 'تم تسجيل دخول الزائر';
            if (reasonKey.includes('exit_recorded')) return 'تم تسجيل خروج الزائر وانتهى التصريح';
            if (visitStatus === 'completed') return 'تصريح الزائر منتهي بنهاية الدوام الرسمي';
            return 'هذا الباركود غير موجود بالنظام أو غير صحيح';
        }

        if (reasonKey.includes('entry recorded')) return `تم تسجيل دخول ${permit?.driverName ?? 'صاحب التصريح'}.`;
        if (reasonKey.includes('exitauthorized recorded')) return `تم خروج ${permit?.driverName ?? 'صاحب التصريح'} بإذن.`;
        if (reasonKey.includes('exitfinal recorded')) return `تم خروج ${permit?.driverName ?? 'صاحب التصريح'} نهائيًا.`;
        if (reasonKey.includes('return recorded')) return `تمت عودة ${permit?.driverName ?? 'صاحب التصريح'}.`;
        if (reasonKey.includes('pendingunauthorizedexit')) return `تم رفض خروج ${permit?.driverName ?? 'صاحب التصريح'} لعدم وجود استئذان، وسجلت الحالة كمحاولة معلقة.`;
        if (reasonKey.includes('unauthorizedexitneedsreview')) return `تم رفع حالة ${permit?.driverName ?? 'صاحب التصريح'} للمراجعة الإدارية.`;
        if (reasonKey.includes('unauthorizedexitstopped')) return `تم إيقاف ${permit?.driverName ?? 'صاحب التصريح'} بعد تكرار الخروج بدون استئذان.`;
        if (reasonKey.includes('overrideentry recorded')) return `تم السماح بدخول ${permit?.driverName ?? 'صاحب التصريح'} استثنائيًا.`;
        if (reasonKey.includes('permit_not_active')) return isVisitorPermit ? 'تصريح الزائر منتهي بعد الخروج' : 'التصريح غير فعال أو منتهي';
        if (reasonKey.includes('expired') || reasonKey.includes('exited')) return isVisitorPermit ? 'تصريح الزائر منتهي بعد الخروج' : 'التصريح منتهي';
        if (permit && (permit?.permitNumber || permit?.driverName)) return 'تمت قراءة التصريح بنجاح.';
        return 'هذا الباركود غير موجود بالنظام أو غير صحيح';
    };

    const renderPermit = (payload) => {
        const permit = payload?.permit;
        const visit = payload?.visit;
        const reason = String(payload?.reason ?? '');
        const allowed = Boolean(payload?.allowed);
        const scanMode = String(payload?.scanMode ?? 'permit');
        const responseOk = Boolean(payload?.responseOk ?? true);

        if (!responseOk && !permit) {
            applyResultTheme('state-danger');
            setChip(permitAccessChip, 'فشل', 'danger');
            setChip(permitStateChip, 'تعذر', 'danger');
            scanHintChip.textContent = 'الباركود غير صالح';
            playTone('error');
            return;
        }

        const reasonKey = normalizeReason(reason);
        const approvalStatusDisplay = String(permit?.approvalStatusDisplay ?? '');
        const visitStatus = normalizeReason(visit?.status);
        const friendlyReason = getFriendlyScanReason(reason, scanMode, permit, visit);

        let movementLabel = 'حركة تصريح';
        let stateTheme = allowed ? 'success' : 'danger';
        let panelTheme = allowed ? 'state-success' : 'state-danger';
        let toneType = allowed ? 'success' : 'error';
        let iconPath = '<path d="M9.55 18 3.85 12.3l1.4-1.4 4.3 4.3 9.2-9.2 1.4 1.4z" />';

        if (scanMode === 'visit') {
            movementLabel = allowed ? 'دخول زائر' : 'زيارة غير معتمدة';
            if (visitStatus === 'completed') {
                movementLabel = 'تصريح زائر منتهي';
                stateTheme = 'warning';
                panelTheme = 'state-danger';
                toneType = 'warning';
            }
        } else if (reasonKey.includes('return')) {
            movementLabel = 'تم السماح بالدخول';
            stateTheme = 'success';
            panelTheme = 'state-success';
            toneType = 'success';
        } else if (reasonKey.includes('entry')) {
            movementLabel = 'دخول مسموح';
        } else if (reasonKey.includes('exitfinal') || reasonKey.includes('exitauthorized')) {
            movementLabel = 'خروج مسموح';
        } else if (reasonKey.includes('pendingunauthorizedexit')) {
            movementLabel = 'رفض - خروج بدون استئذان';
            stateTheme = 'warning';
            panelTheme = 'state-danger';
            toneType = 'warning';
            iconPath = '<path d="M1 21h22L12 2 1 21zm12-3h-2v2h2v-2zm0-8h-2v6h2V10z" />';
        } else if (reasonKey.includes('unauthorizedexitneedsreview')) {
            movementLabel = 'مراجعة إدارية';
            stateTheme = 'warning';
            panelTheme = 'state-danger';
            toneType = 'warning';
            iconPath = '<path d="M1 21h22L12 2 1 21zm12-3h-2v2h2v-2zm0-8h-2v6h2V10z" />';
        } else if (reasonKey.includes('unauthorizedexitstopped')) {
            movementLabel = 'إيقاف بسبب المخالفة';
            iconPath = '<path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z" />';
        } else if (approvalStatusDisplay.includes('منتهي') || reasonKey.includes('expired')) {
            movementLabel = 'تصريح منتهي';
            stateTheme = 'warning';
            panelTheme = 'state-danger';
            toneType = 'warning';
            iconPath = '<path d="M1 21h22L12 2 1 21zm12-3h-2v2h2v-2zm0-8h-2v6h2V10z" />';
        } else if (!allowed) {
            movementLabel = 'رفض';
            iconPath = '<path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z" />';
        }

        applyResultTheme(panelTheme);
        permitEmptyState.hidden = true;
        permitDetailsState.hidden = false;
        setChip(permitAccessChip, allowed ? 'دخول مسموح' : 'رفض', stateTheme);
        setChip(permitStateChip, movementLabel, stateTheme);
        document.getElementById('resultIconSvg').innerHTML = iconPath;

        fields.permitDriverName.textContent = scanMode === 'visit' ? (visit?.visitorName || '-') : (permit?.driverName || '-');
        fields.permitMovementLabel.textContent = friendlyReason || movementLabel;
        fields.permitNumber.textContent = scanMode === 'visit' ? (visit?.visitId || '-') : (permit?.permitNumber || '-');
        fields.permitType.textContent = movementLabel;
        fields.permitDepartment.textContent = scanMode === 'visit' ? (visit?.visitLocation || '-') : (permit?.departmentName || permit?.locationDisplay || '-');
        fields.permitApprovalStatus.textContent = new Date().toLocaleDateString('en-CA');
        fields.permitPlateNumber.textContent = scanMode === 'visit' ? '-' : (permit?.plateNumberDisplay || '-');
        fields.permitTimestamp.textContent = new Date().toLocaleTimeString('ar-IQ', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: true });
        fields.permitSubject.textContent = scanMode === 'visit' ? (visit?.hostName || '-') : (permit?.subject || '-');
        fields.permitReason.textContent = friendlyReason || movementLabel;
        fields.permitNationalId.textContent = scanMode === 'visit' ? (visit?.nationalId || '-') : (permit?.nationalId || '-');
        fields.permitPhone.textContent = scanMode === 'visit' ? (visit?.phoneNumber || '-') : (permit?.employeePhone || '-');
        fields.permitScanSource.textContent = `بواسطة: ${activeOperator?.displayName || '-'}${activeOperator ? '' : ' · يجب تسجيل دخول المشغل'}`;

        if (permit?.permitNumber) {
            lastScannedPermitNumber = permit.permitNumber;
            noteTargetChip.textContent = permit.permitNumber;
            saveOperatorNoteButton.disabled = !isActiveOperatorReady();
            operatorNoteHint.textContent = isActiveOperatorReady()
                ? `ستُحفظ هذه الملاحظة باسم ${activeOperator.displayName} على التصريح ${permit.permitNumber}.`
                : (activeOperator ? 'يجب تغيير PIN المؤقت قبل حفظ أي ملاحظة.' : 'يجب تسجيل دخول مشغل قبل حفظ أي ملاحظة.');
        }

        scanHintChip.textContent = allowed ? 'تمت القراءة بنجاح' : 'توجد ملاحظة على هذه القراءة';
        prependRecentActivity({
            statusText: movementLabel,
            actionLabel: movementLabel,
            driverName: scanMode === 'visit' ? (visit?.visitorName || '') : (permit?.driverName || ''),
            permitNumber: scanMode === 'visit' ? (visit?.visitId || '') : (permit?.permitNumber || ''),
            message: friendlyReason || movementLabel,
            operatorDisplay: activeOperator?.displayName || '-',
            theme: stateTheme === 'success' ? 'success' : stateTheme === 'warning' ? 'warning' : 'danger',
        });
        playTone(toneType);
    };

    const refreshStatus = async () => {
        try {
            const response = await fetch(buildRequestUrl(gateStatusEndpoint, { deviceId }), {
                headers: buildRequestHeaders(),
            });
            const payload = await response.json().catch(() => null);
            if (!payload) {
                return;
            }

            if (Array.isArray(payload.recentActivities) && payload.recentActivities.length) {
                renderRecentActivities(mergeRecentActivities(payload.recentActivities));
            } else {
                renderRecentActivities(displayedRecentActivities);
            }
            updateOperatorUi(payload.activeOperator || null);
            if (statusRefreshHadFailure) {
                statusRefreshHadFailure = false;
                await refreshAntiforgeryToken();
                await loadOperatorStatus();
            }
        } catch {
            statusRefreshHadFailure = true;
            // Ignore transient refresh failures.
        }
    };

    const loadOperatorStatus = async () => {
        try {
            const response = await fetch(buildRequestUrl(operatorStatusEndpoint, { deviceId }), {
                headers: buildRequestHeaders(),
            });
            const payload = await response.json().catch(() => null);
            updateOperatorUi(payload?.operatorSession || null);
            if (payload?.operatorSession?.mustChangePin) {
                openOperatorModal('pin-change');
            }
        } catch {
            updateOperatorUi(null);
        }
    };

    const setActionButtonLabel = (button, nextText) => {
        if (!button) {
            return;
        }

        const label = button.querySelector('span');
        if (label) {
            label.textContent = nextText;
            return;
        }

        button.textContent = nextText;
    };

    const runButtonAction = async (button, pendingText, work) => {
        if (!button) {
            return await work();
        }

        if (button.dataset.pending === 'true') {
            return;
        }

        const originalLabel = button.dataset.originalLabel
            || (button.querySelector('span')?.textContent?.trim() || button.textContent.trim());
        button.dataset.originalLabel = originalLabel;
        button.dataset.pending = 'true';
        button.disabled = true;
        setActionButtonLabel(button, pendingText);

        try {
            return await work();
        } finally {
            button.disabled = false;
            button.dataset.pending = 'false';
            setActionButtonLabel(button, originalLabel);
        }
    };

    const submitOperatorLogin = async () => {
        const badgeCode = operatorBadgeInput.value.trim().toUpperCase();
        const pin = operatorPinInput.value.trim();
        operatorBadgeInput.value = badgeCode;
        if (!badgeCode || pin.length !== 6) {
            setModalMessage('أدخل باركود المشغل والـ PIN المكون من 6 أرقام.', 'danger');
            return;
        }

        if (operatorLoginBusy) {
            return;
        }

        operatorLoginBusy = true;
        await runButtonAction(submitOperatorLoginButton, 'انتظار...', async () => {
            try {
                const { payload } = await postJson(switchOperatorEndpoint, { badgeCode, pin, deviceId });
                if (!payload?.success) {
                    setModalMessage(payload?.message || 'تعذر تسجيل دخول المشغل.', 'danger');
                    return;
                }

                const previousUsername = activeOperator?.username || '';
                const currentOperator = payload.currentOperator || null;
                const currentUsername = currentOperator?.username || '';
                pendingOperatorPin = payload.requiresPinChange ? pin : '';
                updateOperatorUi(currentOperator);
                if (previousUsername !== currentUsername) {
                    clearPermitContext('تم تبديل المشغل. امسح التصريح التالي.');
                }
                if (payload.requiresPinChange) {
                    openOperatorModal('pin-change');
                    return;
                }

                closeOperatorModal();
                await refreshStatus();
            } catch {
                setModalMessage('تعذر الاتصال بالنظام أثناء تبديل المشغل.', 'danger');
            } finally {
                operatorLoginBusy = false;
            }
        });
    };

    const submitOperatorPinChange = async () => {
        const currentPin = operatorCurrentPinInput.value.trim();
        const newPin = operatorNewPinInput.value.trim();
        const confirmPin = operatorConfirmPinInput.value.trim();
        if (currentPin.length !== 6 || newPin.length !== 6 || confirmPin.length !== 6) {
            setModalMessage('أدخل جميع الحقول برمز مكون من 6 أرقام.', 'danger');
            return;
        }
        if (newPin !== confirmPin) {
            setModalMessage('تأكيد الرمز الجديد غير مطابق.', 'danger');
            return;
        }

        if (operatorPinChangeBusy) {
            return;
        }

        operatorPinChangeBusy = true;
        await runButtonAction(submitOperatorPinChangeButton, 'انتظار...', async () => {
            try {
                const { payload } = await postJson(changePinEndpoint, { deviceId, currentPin, newPin });
                if (!payload?.success) {
                    setModalMessage(payload?.message || 'تعذر تحديث الرمز السري.', 'danger');
                    return;
                }

                pendingOperatorPin = '';
                updateOperatorUi(payload.operatorSession || null);
                closeOperatorModal();
                await refreshStatus();
            } catch {
                setModalMessage('تعذر الاتصال بالنظام أثناء تحديث الرمز السري.', 'danger');
            } finally {
                operatorPinChangeBusy = false;
            }
        });
    };

    const signOutOperator = async () => {
        if (!activeOperator) {
            return;
        }

        if (operatorSignOutBusy) {
            return;
        }

        operatorSignOutBusy = true;
        await runButtonAction(signOutOperatorButton, 'انتظار...', async () => {
            try {
                const { payload } = await postJson(signOutOperatorEndpoint, { deviceId });
                if (!payload?.success) {
                    return;
                }

                updateOperatorUi(null);
                pendingOperatorPin = '';
                clearPermitContext('تم تسجيل خروج المشغل. سجّل دخول مشغل للمتابعة.');
                await refreshStatus();
            } catch {
                // Ignore sign-out errors.
            } finally {
                operatorSignOutBusy = false;
            }
        });
    };

    const saveOperatorNote = async () => {
        if (!isActiveOperatorReady() || !lastScannedPermitNumber) {
            return;
        }

        const noteText = operatorNoteInput.value.trim();
        if (!noteText) {
            operatorNoteHint.textContent = 'اكتب الملاحظة أو اختر أحد القوالب السريعة أولاً.';
            return;
        }

        if (noteSaveBusy) {
            return;
        }

        noteSaveBusy = true;
        await runButtonAction(saveOperatorNoteButton, 'انتظار...', async () => {
            try {
                const { payload } = await postJson(saveNoteEndpoint, {
                    deviceId,
                    permitNumber: lastScannedPermitNumber,
                    noteText,
                });

                operatorNoteHint.textContent = payload?.message || 'تم حفظ الملاحظة.';
                if (payload?.success) {
                    prependRecentActivity({
                        statusText: 'ملاحظة',
                        actionLabel: 'ملاحظة',
                        driverName: lastScannedPermitNumber,
                        permitNumber: lastScannedPermitNumber,
                        message: noteText,
                        operatorDisplay: activeOperator?.displayName || '-',
                        theme: 'warning',
                    });
                    operatorNoteInput.value = '';
                    await refreshStatus();
                }
            } catch {
                operatorNoteHint.textContent = 'تعذر حفظ الملاحظة على السجل الحالي.';
            } finally {
                noteSaveBusy = false;
            }
        });
    };

    const lockScanner = (duration = SCAN_COOLDOWN_DELAY) => {
        scanCooldownUntil = Math.max(scanCooldownUntil, Date.now() + duration);
    };

    const canStartScan = () => !scanBusy && Date.now() >= scanCooldownUntil;

    const isRepeatedRecentScan = (value) => {
        const normalizedValue = String(value ?? '').trim();
        if (!normalizedValue) {
            return false;
        }

        return normalizedValue === lastSubmittedScanValue
            && (Date.now() - lastSubmittedScanAt) < REPEATED_SCAN_GUARD_DELAY;
    };

    const rememberSubmittedScan = (value) => {
        lastSubmittedScanValue = String(value ?? '').trim();
        lastSubmittedScanAt = Date.now();
    };

    const resetScannerBuffer = () => {
        scannerBuffer.length = 0;
        if (scannerTimer) {
            window.clearTimeout(scannerTimer);
            scannerTimer = null;
        }
        if (autoSubmitTimer) {
            window.clearTimeout(autoSubmitTimer);
            autoSubmitTimer = null;
        }
    };

    const queueAutoSubmit = () => {
        if (autoSubmitTimer) {
            window.clearTimeout(autoSubmitTimer);
        }

        autoSubmitTimer = window.setTimeout(() => {
            autoSubmitTimer = null;
            if (scannerInput && scannerInput.value.trim() && canStartScan()) {
                resetScannerBuffer();
                submitScan();
            }
        }, 250);
    };

    const maybeHandleOperatorBadgeScan = (rawValue) => {
        const normalizedValue = String(rawValue ?? '').trim().toUpperCase();
        if (!normalizedValue.startsWith('GATE-')) {
            return false;
        }

        openOperatorModal(activeOperator ? 'switch' : 'login', normalizedValue);
        return true;
    };

    const submitScan = async () => {
        if (!canStartScan()) {
            return;
        }

        const rawValue = scannerInput.value.trim();
        if (!rawValue) {
            return;
        }

        if (maybeHandleOperatorBadgeScan(rawValue)) {
            resetScannerBuffer();
            scannerInput.value = '';
            return;
        }

        if (!isActiveOperatorReady()) {
            scanHintChip.textContent = activeOperator
                ? 'يجب تغيير PIN المؤقت قبل البدء بالمسح'
                : 'يجب تسجيل دخول مشغل قبل البدء بالمسح';
            openOperatorModal(activeOperator ? 'pin-change' : 'login');
            resetScannerBuffer();
            scannerInput.value = '';
            return;
        }

        if (isRepeatedRecentScan(rawValue)) {
            resetScannerBuffer();
            scannerInput.value = '';
            lockScanner();
            setChip(permitAccessChip, 'مكرر', 'warning');
            setChip(permitStateChip, 'محجوز', 'warning');
            scanHintChip.textContent = 'تم تجاهل قراءة مكررة لنفس الباركود';
            return;
        }

        scanBusy = true;
        rememberSubmittedScan(rawValue);
        scanHintChip.textContent = 'جاري قراءة الباركود';

        try {
            const response = await fetch(buildRequestUrl(scanEndpoint), {
                method: 'POST',
                headers: buildRequestHeaders({ includeJson: true }),
                body: JSON.stringify({
                    identifier: rawValue,
                    scannerUserId: activeOperator.username,
                    gateName: 'شاشة البوابة',
                    executionMethod: 'scan',
                    deviceId,
                }),
            });

            const payload = await response.json().catch(() => null);
            if (!payload) {
                throw new Error('scan_failed');
            }

            renderPermit({ ...payload, responseOk: response.ok });
            await refreshStatus();
        } catch {
            applyResultTheme('state-danger');
            setChip(permitAccessChip, 'فشل', 'danger');
            setChip(permitStateChip, 'تعذر', 'danger');
            scanHintChip.textContent = 'تعذر الاتصال بالنظام أو قراءة الباركود';
            playTone('error');
        } finally {
            resetScannerBuffer();
            scannerInput.value = '';
            scannerInput.focus();
            scanBusy = false;
            lockScanner();
        }
    };

    const focusScanner = () => {
        scannerInput.focus({ preventScroll: true });
        scannerInput.select();
    };

    const setCameraStatus = (message, isError = false) => {
        if (!gateCameraStatus) {
            return;
        }

        gateCameraStatus.textContent = message;
        gateCameraStatus.classList.toggle('is-error', isError);
    };

    const stopCameraScanner = (message = '') => {
        cameraRunning = false;
        if (cameraFrameRequest) {
            window.cancelAnimationFrame(cameraFrameRequest);
            cameraFrameRequest = 0;
        }

        if (cameraFallbackControls) {
            cameraFallbackControls.stop();
            cameraFallbackControls = null;
        }

        if (cameraStream) {
            cameraStream.getTracks().forEach((track) => track.stop());
            cameraStream = null;
        }

        if (gateCameraPreview) {
            gateCameraPreview.srcObject = null;
        }

        if (gateCameraPanel) {
            gateCameraPanel.hidden = true;
        }

        if (openGateCameraButton) {
            openGateCameraButton.disabled = false;
        }

        if (message) {
            setCameraStatus(message);
        }

        focusScanner();
    };

    const handleCameraValue = (rawValue) => {
        const value = String(rawValue || '').trim();
        if (!value || !cameraRunning) {
            return;
        }

        scannerInput.value = value;
        resetScannerBuffer();
        stopCameraScanner('تمت قراءة الرمز من الكاميرا');
        submitScan();
    };

    const detectCameraFrame = async () => {
        if (!cameraRunning || !cameraDetector || !gateCameraPreview) {
            return;
        }

        try {
            const codes = await cameraDetector.detect(gateCameraPreview);
            const rawValue = codes && codes.length ? String(codes[0].rawValue || '').trim() : '';
            if (rawValue) {
                handleCameraValue(rawValue);
                return;
            }
        } catch {
            setCameraStatus('تعذر تحليل الصورة. قرب الكاميرا من الرمز أو استخدم قارئ الباركود.', true);
        }

        cameraFrameRequest = window.requestAnimationFrame(detectCameraFrame);
    };

    const startCameraScanner = async () => {
        if (!openGateCameraButton || !gateCameraPanel || !gateCameraPreview) {
            return;
        }

        if (!window.isSecureContext && !['localhost', '127.0.0.1'].includes(window.location.hostname)) {
            gateCameraPanel.hidden = false;
            setCameraStatus('قراءة الكاميرا على الجوال تحتاج تشغيل الموقع عبر HTTPS.', true);
            return;
        }

        if (!navigator.mediaDevices?.getUserMedia) {
            gateCameraPanel.hidden = false;
            setCameraStatus('هذا المتصفح لا يسمح بتشغيل الكاميرا. افتح الرابط في Safari أو Chrome واسمح بإذن الكاميرا.', true);
            return;
        }

        try {
            openGateCameraButton.disabled = true;
            gateCameraPanel.hidden = false;
            setCameraStatus('جاري تشغيل كاميرا الجوال...');

            const constraints = {
                video: { facingMode: { ideal: 'environment' } },
                audio: false,
            };
            cameraRunning = true;

            if ('BarcodeDetector' in window) {
                const formats = ['qr_code', 'code_128', 'code_39', 'ean_13', 'ean_8', 'itf', 'upc_a', 'upc_e'];
                const supportedFormats = typeof BarcodeDetector.getSupportedFormats === 'function'
                    ? await BarcodeDetector.getSupportedFormats()
                    : formats;
                const activeFormats = formats.filter((format) => supportedFormats.includes(format));

                cameraDetector = new BarcodeDetector(activeFormats.length ? { formats: activeFormats } : undefined);
                cameraStream = await navigator.mediaDevices.getUserMedia(constraints);
                gateCameraPreview.srcObject = cameraStream;
                await gateCameraPreview.play();
                setCameraStatus('وجّه كاميرا الجوال نحو QR أو الباركود.');
                cameraFrameRequest = window.requestAnimationFrame(detectCameraFrame);
                return;
            }

            if (!window.ZXingBrowser?.BrowserMultiFormatReader) {
                throw new Error('camera_reader_unavailable');
            }

            cameraDetector = null;
            const fallbackReader = new window.ZXingBrowser.BrowserMultiFormatReader(undefined, {
                delayBetweenScanAttempts: 120,
            });
            cameraFallbackControls = await fallbackReader.decodeFromConstraints(
                constraints,
                gateCameraPreview,
                (result) => {
                    if (result && cameraRunning) {
                        handleCameraValue(result.getText ? result.getText() : result.text);
                    }
                }
            );
            if (!cameraRunning) {
                cameraFallbackControls.stop();
                cameraFallbackControls = null;
                return;
            }
            setCameraStatus('وجّه كاميرا الجوال نحو QR أو الباركود.');
        } catch (error) {
            stopCameraScanner();
            if (gateCameraPanel) {
                gateCameraPanel.hidden = false;
            }
            const permissionDenied = error?.name === 'NotAllowedError';
            setCameraStatus(
                permissionDenied
                    ? 'تم رفض إذن الكاميرا. اسمح للموقع باستخدامها من إعدادات المتصفح ثم أعد المحاولة.'
                    : 'تعذر تشغيل الكاميرا. أغلق أي تطبيق يستخدمها ثم أعد المحاولة.',
                true
            );
        }
    };

    kioskDeviceLabel.textContent = deviceId.toUpperCase();
    kioskIpLabel.textContent = window.location.hostname || '--';

    notePresetButtons.forEach((button) => {
        button.addEventListener('click', () => {
            const preset = button.dataset.notePreset || '';
            operatorNoteInput.value = operatorNoteInput.value.trim()
                ? `${operatorNoteInput.value.trim()} ${preset}`
                : preset;
            operatorNoteInput.focus({ preventScroll: true });
        });
    });

    focusScannerButton.addEventListener('click', focusScanner);
    openGateCameraButton?.addEventListener('click', startCameraScanner);
    stopGateCameraButton?.addEventListener('click', () => stopCameraScanner('تم إيقاف الكاميرا.'));
    openOperatorModalButton.addEventListener('click', () => openOperatorModal(
        activeOperator?.mustChangePin ? 'pin-change' : (activeOperator ? 'switch' : 'login')
    ));
    signOutOperatorButton.addEventListener('click', signOutOperator);
    closeOperatorModalButton.addEventListener('click', closeOperatorModal);
    cancelOperatorModalButton.addEventListener('click', closeOperatorModal);
    submitOperatorLoginButton.addEventListener('click', submitOperatorLogin);
    submitOperatorPinChangeButton.addEventListener('click', submitOperatorPinChange);
    saveOperatorNoteButton.addEventListener('click', saveOperatorNote);
    permitResultCard.addEventListener('click', focusScanner);

    document.addEventListener('keydown', (event) => {
        if (event.defaultPrevented) {
            return;
        }

        if (!operatorModal.classList.contains('is-hidden')) {
            if (handleOperatorModalKeydown(event)) {
                return;
            }
            return;
        }

        if (isInteractiveTypingTarget(event.target)) {
            return;
        }

        if (event.key === 'Escape') {
            event.preventDefault();
            resetScannerBuffer();
            scannerInput.value = '';
            focusScanner();
            return;
        }

        if (event.key === 'Enter') {
            event.preventDefault();
            if (!canStartScan()) {
                return;
            }
            resetScannerBuffer();
            submitScan();
            return;
        }

        if (!event.ctrlKey && !event.metaKey && !event.altKey) {
            const englishCharacter = getEnglishCharacterFromEvent(event);
            if (!englishCharacter) {
                return;
            }

            if (!canStartScan()) {
                event.preventDefault();
                return;
            }

            event.preventDefault();
            scannerBuffer.push(englishCharacter);
            scannerInput.value = scannerBuffer.join('');

            if (scannerTimer) {
                window.clearTimeout(scannerTimer);
            }

            scannerTimer = window.setTimeout(resetScannerBuffer, 500);
            queueAutoSubmit();
        }
    }, true);

    scannerInput.addEventListener('input', queueAutoSubmit);
    scannerInput.addEventListener('paste', () => {
        if (!canStartScan()) {
            return;
        }
        window.requestAnimationFrame(() => {
            if (scannerInput.value.trim() && canStartScan()) {
                submitScan();
            }
        });
    });

    window.addEventListener('load', async () => {
        resetResult();
        renderRecentActivities(initialRecentActivities);
        await loadOperatorStatus();
        await refreshStatus();
        focusScanner();
        window.setInterval(refreshStatus, STATUS_REFRESH_INTERVAL);
    });

    window.addEventListener('focus', focusScanner);
    window.addEventListener('pagehide', () => stopCameraScanner());
})();
