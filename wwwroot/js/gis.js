window.gis = {
    _dotNetRef: null,
    _popupClient: null,
    _redirectClient: null,

    init: (clientId, redirectUri, dotNetRef) => {
        window.gis._dotNetRef = dotNetRef;

        window.gis._popupClient = google.accounts.oauth2.initTokenClient({
            client_id: clientId,
            scope: 'https://www.googleapis.com/auth/drive.appdata',
            callback: (response) => {
                if (response.error) {
                    window.gis._dotNetRef.invokeMethodAsync('OnAuthError', response.error);
                } else {
                    window.gis._dotNetRef.invokeMethodAsync('OnAuthSuccess', response.access_token);
                }
            }
        });

        window.gis._redirectClient = google.accounts.oauth2.initTokenClient({
            client_id: clientId,
            scope: 'https://www.googleapis.com/auth/drive.appdata',
            ux_mode: 'redirect',
            redirect_uri: redirectUri,
            callback: () => {}
        });
    },

    connect: () => {
        const esMobil = /iPhone|iPad|iPod|Android/i.test(navigator.userAgent);
        if (esMobil) {
            window.gis._redirectClient.requestAccessToken();
        } else {
            window.gis._popupClient.requestAccessToken();
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
        google.accounts.oauth2.revoke(token, () => {});
    }
};
