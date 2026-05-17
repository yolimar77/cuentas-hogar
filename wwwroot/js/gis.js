window.registerVisibilityHandler = (dotNetRef) => {
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            dotNetRef.invokeMethodAsync('OnAppVisible');
        }
    });
};

window.gis = {
    _dotNetRef: null,
    _popupClient: null,
    _clientId: null,
    _redirectUri: null,

    init: (clientId, redirectUri, dotNetRef) => {
        window.gis._clientId = clientId;
        window.gis._redirectUri = redirectUri;
        window.gis._dotNetRef = dotNetRef;

        // Intentar inicializar cliente popup (solo escritorio, puede fallar si GIS no cargó aún)
        try {
            if (typeof google !== 'undefined' && google.accounts) {
                window.gis._popupClient = google.accounts.oauth2.initTokenClient({
                    client_id: clientId,
                    scope: 'https://www.googleapis.com/auth/drive',
                    callback: (response) => {
                        if (response.error) {
                            window.gis._dotNetRef.invokeMethodAsync('OnAuthError', response.error);
                        } else {
                            window.gis._dotNetRef.invokeMethodAsync('OnAuthSuccess', response.access_token);
                        }
                    }
                });
            }
        } catch (e) {
            console.warn('GIS popup init fallido:', e);
        }
    },

    connect: () => {
        const esMobil = /iPhone|iPad|iPod|Android/i.test(navigator.userAgent);
        if (esMobil) {
            // Redireccionamiento directo sin depender de la librería GIS
            const params = new URLSearchParams({
                client_id: window.gis._clientId,
                redirect_uri: window.gis._redirectUri,
                response_type: 'token',
                scope: 'https://www.googleapis.com/auth/drive'
            });
            window.location.href = 'https://accounts.google.com/o/oauth2/v2/auth?' + params.toString();
        } else {
            // Inicialización perezosa: la librería GIS puede no estar lista cuando init() se llamó
            if (!window.gis._popupClient && typeof google !== 'undefined' && google.accounts) {
                window.gis._popupClient = google.accounts.oauth2.initTokenClient({
                    client_id: window.gis._clientId,
                    scope: 'https://www.googleapis.com/auth/drive',
                    callback: (response) => {
                        if (response.error) {
                            window.gis._dotNetRef.invokeMethodAsync('OnAuthError', response.error);
                        } else {
                            window.gis._dotNetRef.invokeMethodAsync('OnAuthSuccess', response.access_token);
                        }
                    }
                });
            }

            if (window.gis._popupClient) {
                window.gis._popupClient.requestAccessToken();
            } else {
                console.error('Cliente OAuth no disponible');
            }
        }
    },

    checkRedirectToken: () => {
        const hash = window.location.hash;
        if (!hash) return null;
        const params = new URLSearchParams(hash.substring(1));
        const token = params.get('access_token');
        if (token) {
            history.replaceState(null, '', window.location.pathname + window.location.search);
            return token;
        }
        return null;
    },

    silentRefresh: () => {
        const esMobil = /iPhone|iPad|iPod|Android/i.test(navigator.userAgent);
        if (esMobil) return Promise.reject('mobile');

        return new Promise((resolve, reject) => {
            if (typeof google === 'undefined' || !google.accounts) {
                reject('gis_not_available');
                return;
            }
            try {
                const timeout = setTimeout(() => reject('timeout'), 12000);
                const client = google.accounts.oauth2.initTokenClient({
                    client_id: window.gis._clientId,
                    scope: 'https://www.googleapis.com/auth/drive',
                    callback: (resp) => {
                        clearTimeout(timeout);
                        if (resp.error) reject(resp.error);
                        else resolve(resp.access_token);
                    },
                    error_callback: (err) => {
                        clearTimeout(timeout);
                        reject(err?.type ?? 'error');
                    }
                });
                client.requestAccessToken({ prompt: '' });
            } catch (e) {
                reject(String(e));
            }
        });
    },

    disconnect: (token) => {
        try {
            if (typeof google !== 'undefined' && google.accounts) {
                google.accounts.oauth2.revoke(token, () => {});
            }
        } catch (e) { }
    }
};
