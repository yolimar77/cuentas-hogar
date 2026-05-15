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
        } else if (window.gis._popupClient) {
            window.gis._popupClient.requestAccessToken();
        } else {
            console.error('Cliente OAuth no disponible');
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

    disconnect: (token) => {
        try {
            if (typeof google !== 'undefined' && google.accounts) {
                google.accounts.oauth2.revoke(token, () => {});
            }
        } catch (e) { }
    }
};
