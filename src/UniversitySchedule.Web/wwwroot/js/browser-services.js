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
            const store = database.transaction(storeName, 'readwrite').objectStore(storeName);
            const request = key === undefined ? store.put(value) : store.put(value, key);
            request.onsuccess = () => resolve();
            request.onerror = () => reject(request.error);
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
