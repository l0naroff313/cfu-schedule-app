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
})();
