(() => {
    const databaseName = 'cfu-eljournal';
    const databaseVersion = 1;
    let databasePromise;

    function openDatabase() {
        if (databasePromise) return databasePromise;
        databasePromise = new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, databaseVersion);
            request.onupgradeneeded = () => {
                const database = request.result;
                if (!database.objectStoreNames.contains('documents')) {
                    database.createObjectStore('documents', { keyPath: 'key' });
                }
                if (!database.objectStoreNames.contains('secrets')) {
                    database.createObjectStore('secrets');
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        return databasePromise;
    }

    async function read(storeName, key) {
        const database = await openDatabase();
        return new Promise((resolve, reject) => {
            const request = database.transaction(storeName, 'readonly').objectStore(storeName).get(key);
            request.onsuccess = () => resolve(request.result ?? null);
            request.onerror = () => reject(request.error);
        });
    }

    async function write(storeName, value, key) {
        const database = await openDatabase();
        return new Promise((resolve, reject) => {
            const transaction = database.transaction(storeName, 'readwrite');
            const store = transaction.objectStore(storeName);
            if (key === undefined) store.put(value); else store.put(value, key);
            transaction.oncomplete = () => resolve();
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error ?? new Error('Запись данных отменена.'));
        });
    }

    async function remove(storeName, key) {
        const database = await openDatabase();
        return new Promise((resolve, reject) => {
            const request = database.transaction(storeName, 'readwrite').objectStore(storeName).delete(key);
            request.onsuccess = () => resolve();
            request.onerror = () => reject(request.error);
        });
    }

    window.cfuStorage = {
        getDocument: key => read('documents', key),
        saveDocument: document => write('documents', document),
        getSecret: key => read('secrets', key),
        setSecret: (key, value) => write('secrets', value, key),
        removeSecret: key => remove('secrets', key)
    };

    window.cfuTheme = {
        get: () => document.documentElement.dataset.theme,
        apply: theme => {
            document.documentElement.dataset.theme = theme;
            localStorage.setItem('cfu.web.theme.v1', theme);
            const themeColor = document.querySelector('meta[name="theme-color"]');
            themeColor?.setAttribute('content', theme === 'dark' ? '#00182d' : '#f5f7fb');
        }
    };

    window.cfuPwa = {
        isStandalone: () => matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true
    };

    const notificationTimers = new Map();
    const notificationTimerLimit = 2147483647;
    let notificationGeneration = 0;

    function armNotification(item, generation) {
        const triggerAt = new Date(item.triggerAt).getTime();
        if (!Number.isFinite(triggerAt)) return;
        const delay = triggerAt - Date.now();
        if (delay <= 0) return;
        const timer = setTimeout(async () => {
            if (generation !== notificationGeneration) return;
            notificationTimers.delete(item.id);
            if (delay > notificationTimerLimit) {
                armNotification(item, generation);
                return;
            }
            if (Notification.permission !== 'granted') return;
            try {
                const registration = await navigator.serviceWorker.getRegistration(document.baseURI);
                if (!registration?.active || generation !== notificationGeneration) return;
                const deadline = new Date(item.deadline).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short', timeZone: 'Europe/Moscow' });
                await registration.showNotification(`Дедлайн: ${item.subject || 'Домашнее задание'}`, {
                    body: `${item.text} • дедлайн ${deadline} (МСК)`,
                    tag: `cfu-assignment-${item.id}`,
                    icon: new URL('icons/appicon-192.png', document.baseURI).href,
                    data: { url: document.baseURI },
                });
            } catch (error) {
                console.warn('Не удалось показать напоминание:', error);
            }
        }, Math.min(delay, notificationTimerLimit));
        notificationTimers.set(item.id, timer);
    }

    window.cfuNotifications = {
        requestPermission: async () => {
            if (!('Notification' in window) || !('serviceWorker' in navigator)) return 'unsupported';
            try {
                // Request immediately within the user's tap, before awaiting worker readiness.
                const permission = await Notification.requestPermission();
                if (permission !== 'granted') return permission;
                const registration = await navigator.serviceWorker.getRegistration(document.baseURI);
                return registration?.active && typeof registration.showNotification === 'function' ? 'granted' : 'not-ready';
            } catch { return 'unsupported'; }
        },
        schedule: items => {
            for (const timer of notificationTimers.values()) clearTimeout(timer);
            notificationTimers.clear();
            const generation = ++notificationGeneration;
            if (!('Notification' in window) || Notification.permission !== 'granted') return;
            for (const item of (items || [])) {
                if (!item) continue;
                const normalized = { ...item, id: item.id || `${item.subject || ''}:${item.deadline || ''}` };
                armNotification(normalized, generation);
            }
        }
    };

    function emptyOfflineStatus(error = null) {
        return {
            isSupported: 'serviceWorker' in navigator && 'caches' in window,
            isReady: false,
            cachedAssetCount: 0,
            missingAssetCount: 0,
            error
        };
    }

    async function requestOfflineWorker(type) {
        if (!('serviceWorker' in navigator) || !('caches' in window)) {
            return emptyOfflineStatus('Автономный режим не поддерживается этим браузером.');
        }

        const registration = await navigator.serviceWorker.getRegistration(document.baseURI);
        const worker = navigator.serviceWorker.controller;
        if (!registration?.active || !worker || worker.state !== 'activated') {
            return emptyOfflineStatus('Файлы приложения ещё устанавливаются. Откройте приложение повторно с интернетом.');
        }
        if (registration.waiting) {
            return emptyOfflineStatus('Доступно обновление. Закройте все окна приложения и откройте его снова с интернетом.');
        }

        return await new Promise((resolve, reject) => {
            const channel = new MessageChannel();
            const close = () => {
                clearTimeout(timeoutId);
                channel.port1.close();
            };
            const timeoutId = setTimeout(() => {
                close();
                reject(new Error('Не удалось проверить все файлы. Повторите загрузку с интернетом.'));
            }, type === 'CFU_PREPARE_OFFLINE' ? 60000 : 10000);
            channel.port1.onmessage = event => {
                close();
                const status = event.data;
                if (status?.protocolVersion !== 2) {
                    resolve(emptyOfflineStatus('Обновите приложение: закройте все его окна и откройте снова с интернетом.'));
                    return;
                }
                resolve({
                    ...status,
                    isSupported: true,
                    isReady: status.isReady === true && status.cachedAssetCount > 0 &&
                        status.missingAssetCount === 0 && navigator.serviceWorker.controller === worker
                });
            };
            worker.postMessage({ type }, [channel.port2]);
        });
    }

    window.cfuOffline = {
        getStatus: async () => {
            try {
                return await requestOfflineWorker('CFU_CHECK_OFFLINE');
            } catch (error) {
                return emptyOfflineStatus(error instanceof Error ? error.message : String(error));
            }
        },
        prepare: async () => {
            if (!('serviceWorker' in navigator) || !('caches' in window)) {
                return emptyOfflineStatus('Автономный режим не поддерживается этим браузером.');
            }

            try {
                return await requestOfflineWorker('CFU_PREPARE_OFFLINE');
            } catch (error) {
                return emptyOfflineStatus(error instanceof Error ? error.message : String(error));
            }
        }
    };

    const swipeRegistrations = new Map();
    let nextSwipeRegistrationId = 1;
    let lastSwipeInvocationAt = 0;

    function isInteractiveSwipeTarget(target) {
        return target instanceof Element && target.closest(
            'input, textarea, select, button, a, [role="dialog"], [contenteditable="true"], [data-swipe-ignore]');
    }

    window.cfuSwipe = {
        attach: (element, dotNetReference) => {
            if (!element) return 0;

            const registrationId = nextSwipeRegistrationId++;
            let pointerId = null;
            let startX = 0;
            let startY = 0;
            let startedAt = 0;
            let invoking = false;

            const reset = () => {
                pointerId = null;
                startX = 0;
                startY = 0;
                startedAt = 0;
            };

            const onPointerDown = event => {
                if (!event.isPrimary || invoking || isInteractiveSwipeTarget(event.target)) return;
                if (event.pointerType === 'mouse' && event.buttons !== 1) return;

                pointerId = event.pointerId;
                startX = event.clientX;
                startY = event.clientY;
                startedAt = performance.now();
            };

            const onPointerUp = event => {
                if (event.pointerId !== pointerId) return;

                const distanceX = event.clientX - startX;
                const distanceY = event.clientY - startY;
                const duration = performance.now() - startedAt;
                reset();

                const isHorizontal = Math.abs(distanceX) >= 56 && Math.abs(distanceX) > Math.abs(distanceY) * 1.35;
                if (!isHorizontal || duration > 700) return;

                const invokedAt = performance.now();
                if (invokedAt - lastSwipeInvocationAt < 450) return;
                lastSwipeInvocationAt = invokedAt;
                invoking = true;
                const direction = distanceX < 0 ? 1 : -1;
                dotNetReference.invokeMethodAsync('OnTabSwipe', direction)
                    .catch(() => undefined)
                    .finally(() => { invoking = false; });
            };

            const onPointerCancel = event => {
                if (event.pointerId === pointerId) reset();
            };

            element.addEventListener('pointerdown', onPointerDown, { passive: true });
            element.addEventListener('pointerup', onPointerUp, { passive: true });
            element.addEventListener('pointercancel', onPointerCancel, { passive: true });
            swipeRegistrations.set(registrationId, { element, onPointerDown, onPointerUp, onPointerCancel });
            return registrationId;
        },
        detach: registrationId => {
            const registration = swipeRegistrations.get(registrationId);
            if (!registration) return;

            registration.element.removeEventListener('pointerdown', registration.onPointerDown);
            registration.element.removeEventListener('pointerup', registration.onPointerUp);
            registration.element.removeEventListener('pointercancel', registration.onPointerCancel);
            swipeRegistrations.delete(registrationId);
        }
    };
})();
